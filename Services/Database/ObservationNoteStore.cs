using Microsoft.Data.Sqlite;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;

namespace CanfarDesktop.Services.Database;

/// <summary>
/// SQLite-backed store for per-observation research notes/ratings/tags, keyed by publisher ID,
/// with FTS5 full-text search over note text and tags. Writing an empty note removes the row;
/// explicit deletes are soft (for future sync). All access is serialized on a single connection.
/// </summary>
public class ObservationNoteStore
{
    private const char TagSeparator = ';';

    private readonly SqliteConnection _connection;
    private readonly object _gate = new();

    public ObservationNoteStore(AppDatabase database) => _connection = database.Connection;

    public ObservationNote? Get(string publisherID)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT note, rating, tags, updatedUtc, agentAttribution FROM notes WHERE publisherID = $id AND deleted = 0;";
            cmd.Parameters.AddWithValue("$id", publisherID);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            return new ObservationNote
            {
                PublisherID = publisherID,
                Note = r.GetString(0),
                Rating = r.GetInt32(1),
                Tags = SplitTags(r.GetString(2)),
                UpdatedUtc = ParseUtc(r.GetString(3)),
                AgentAttribution = ParseAttribution(r.IsDBNull(4) ? null : r.GetString(4)),
            };
        }
    }

    /// <summary>Insert or update a note. An empty note (blank, unrated, no tags) removes the row.</summary>
    public void Upsert(ObservationNote note)
    {
        lock (_gate)
        {
            if (note.IsEmpty)
            {
                DeleteHard(note.PublisherID);
                return;
            }

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO notes (publisherID, note, rating, tags, updatedUtc, deleted, agentAttribution)
                VALUES ($id, $note, $rating, $tags, $updated, 0, $attribution)
                ON CONFLICT(publisherID) DO UPDATE SET
                    note = excluded.note, rating = excluded.rating, tags = excluded.tags,
                    updatedUtc = excluded.updatedUtc, deleted = 0,
                    agentAttribution = excluded.agentAttribution;
                """;
            cmd.Parameters.AddWithValue("$id", note.PublisherID);
            cmd.Parameters.AddWithValue("$note", note.Note);
            cmd.Parameters.AddWithValue("$rating", note.Rating);
            cmd.Parameters.AddWithValue("$tags", JoinTags(note.Tags));
            cmd.Parameters.AddWithValue("$updated", FormatUtc(note.UpdatedUtc));
            cmd.Parameters.AddWithValue("$attribution", (object?)FormatAttribution(note.AgentAttribution) ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Soft-delete (keeps the row with deleted = 1 for future sync reconciliation).</summary>
    public void Delete(string publisherID)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE notes SET deleted = 1 WHERE publisherID = $id;";
            cmd.Parameters.AddWithValue("$id", publisherID);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Publisher IDs whose note text or tags match the query (FTS5 prefix), best matches first.</summary>
    public IReadOnlyList<string> SearchPublisherIds(string query)
    {
        var match = FtsQuery.BuildPrefix(query);
        if (match.Length == 0) return Array.Empty<string>();

        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT n.publisherID FROM notes n
                JOIN noteSearch s ON s.rowid = n.rowid
                WHERE noteSearch MATCH $q AND n.deleted = 0
                ORDER BY rank;
                """;
            cmd.Parameters.AddWithValue("$q", match);
            using var r = cmd.ExecuteReader();
            var ids = new List<string>();
            while (r.Read()) ids.Add(r.GetString(0));
            return ids;
        }
    }

    public IReadOnlyList<ObservationNote> All()
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT publisherID, note, rating, tags, updatedUtc, agentAttribution FROM notes WHERE deleted = 0;";
            using var r = cmd.ExecuteReader();
            var list = new List<ObservationNote>();
            while (r.Read())
                list.Add(new ObservationNote
                {
                    PublisherID = r.GetString(0),
                    Note = r.GetString(1),
                    Rating = r.GetInt32(2),
                    Tags = SplitTags(r.GetString(3)),
                    UpdatedUtc = ParseUtc(r.GetString(4)),
                    AgentAttribution = ParseAttribution(r.IsDBNull(5) ? null : r.GetString(5)),
                });
            return list;
        }
    }

    private static string? FormatAttribution(AgentAttribution? attribution)
        => attribution is null ? null : System.Text.Json.JsonSerializer.Serialize(attribution);

    private static AgentAttribution? ParseAttribution(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return System.Text.Json.JsonSerializer.Deserialize<AgentAttribution>(json); }
        catch { return null; } // a corrupt stamp must never block loading the note
    }

    private void DeleteHard(string publisherID)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM notes WHERE publisherID = $id;";
        cmd.Parameters.AddWithValue("$id", publisherID);
        cmd.ExecuteNonQuery();
    }

    private static string JoinTags(IReadOnlyList<string> tags)
        => string.Join(TagSeparator, tags
            .Select(t => t.Trim().Replace(TagSeparator.ToString(), string.Empty))
            .Where(t => t.Length > 0));

    private static IReadOnlyList<string> SplitTags(string raw)
        => string.IsNullOrEmpty(raw) ? Array.Empty<string>() : raw.Split(TagSeparator, StringSplitOptions.RemoveEmptyEntries);

    private static string FormatUtc(DateTimeOffset d)
        => d == default ? string.Empty : d.ToUniversalTime().ToString("O");

    private static DateTimeOffset ParseUtc(string s)
        => DateTimeOffset.TryParse(s, null,
               System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
               out var d) ? d : default;
}
