using System.Globalization;
using Microsoft.Data.Sqlite;
using CanfarDesktop.Services.Database;

namespace CanfarDesktop.Services.AiGuide;

/// <summary>
/// SQLite-backed store for the AI Guide: per-tool description overrides (<c>aiToolOverride</c>) and
/// user-authored guide tools (<c>aiGuideTool</c>). Mirrors the macOS GRDB v2 schema. A built-in
/// tool's description is the single source of truth and is NEVER stored — an override is a sparse
/// delta and "reset" is a soft-delete. Guide-name uniqueness among LIVE rows is enforced by
/// <see cref="AiGuideService"/>, not a DB constraint, so a deleted name can be reused. All access is
/// serialized on the single app connection.
/// </summary>
public sealed class AiGuideStore
{
    private readonly SqliteConnection _connection;
    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _clock;
    private readonly string _deviceId;

    public AiGuideStore(AppDatabase database, Func<DateTimeOffset>? clock = null, string? deviceId = null)
    {
        _connection = database.Connection;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _deviceId = deviceId ?? DeviceId;
    }

    // MARK: - Read

    public Dictionary<string, string> LoadOverrides()
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT toolName, userDescription FROM aiToolOverride WHERE deletedAt IS NULL;";
            using var r = cmd.ExecuteReader();
            var map = new Dictionary<string, string>();
            while (r.Read()) map[r.GetString(0)] = r.GetString(1);
            return map;
        }
    }

    public List<AiGuideToolEntry> LoadGuides()
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT uuid, name, description, body FROM aiGuideTool
                WHERE deletedAt IS NULL ORDER BY orderIndex ASC, name ASC;
                """;
            using var r = cmd.ExecuteReader();
            var list = new List<AiGuideToolEntry>();
            while (r.Read())
            {
                if (!Guid.TryParse(r.GetString(0), out var id)) continue;
                var body = r.IsDBNull(3) ? null : r.GetString(3);
                list.Add(new AiGuideToolEntry(id, r.GetString(1), r.GetString(2), body));
            }
            return list;
        }
    }

    // MARK: - Override writes

    /// <summary>Insert or revive an override for a built-in tool (bumps version on conflict).</summary>
    public void UpsertOverride(string toolName, string description)
    {
        lock (_gate)
        {
            var now = Now();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO aiToolOverride
                    (uuid, toolName, userDescription, createdAt, updatedAt, version, deletedAt, lastWriterDeviceID)
                VALUES ($uuid, $name, $desc, $now, $now, 1, NULL, $dev)
                ON CONFLICT(toolName) DO UPDATE SET
                    userDescription = excluded.userDescription,
                    updatedAt = excluded.updatedAt,
                    version = aiToolOverride.version + 1,
                    deletedAt = NULL,
                    lastWriterDeviceID = excluded.lastWriterDeviceID;
                """;
            cmd.Parameters.AddWithValue("$uuid", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("$name", toolName);
            cmd.Parameters.AddWithValue("$desc", description);
            cmd.Parameters.AddWithValue("$now", now);
            cmd.Parameters.AddWithValue("$dev", _deviceId);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Reset a tool to its built-in description (soft-delete the override row).</summary>
    public void SoftDeleteOverride(string toolName)
    {
        lock (_gate)
        {
            var now = Now();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                UPDATE aiToolOverride
                SET deletedAt = $now, updatedAt = $now, version = version + 1, lastWriterDeviceID = $dev
                WHERE toolName = $name AND deletedAt IS NULL;
                """;
            cmd.Parameters.AddWithValue("$now", now);
            cmd.Parameters.AddWithValue("$dev", _deviceId);
            cmd.Parameters.AddWithValue("$name", toolName);
            cmd.ExecuteNonQuery();
        }
    }

    // MARK: - Guide writes

    public void InsertGuide(AiGuideToolEntry entry, int orderIndex)
    {
        lock (_gate)
        {
            var now = Now();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO aiGuideTool
                    (uuid, name, description, body, orderIndex, createdAt, updatedAt, version, deletedAt, lastWriterDeviceID)
                VALUES ($uuid, $name, $desc, $body, $order, $now, $now, 1, NULL, $dev);
                """;
            cmd.Parameters.AddWithValue("$uuid", entry.Id.ToString());
            cmd.Parameters.AddWithValue("$name", entry.Name);
            cmd.Parameters.AddWithValue("$desc", entry.Description);
            cmd.Parameters.AddWithValue("$body", (object?)entry.Body ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$order", orderIndex);
            cmd.Parameters.AddWithValue("$now", now);
            cmd.Parameters.AddWithValue("$dev", _deviceId);
            cmd.ExecuteNonQuery();
        }
    }

    public void UpdateGuide(Guid id, string name, string description, string? body)
    {
        lock (_gate)
        {
            var now = Now();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                UPDATE aiGuideTool
                SET name = $name, description = $desc, body = $body, updatedAt = $now,
                    version = version + 1, lastWriterDeviceID = $dev
                WHERE uuid = $uuid AND deletedAt IS NULL;
                """;
            cmd.Parameters.AddWithValue("$name", name);
            cmd.Parameters.AddWithValue("$desc", description);
            cmd.Parameters.AddWithValue("$body", (object?)body ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$now", now);
            cmd.Parameters.AddWithValue("$dev", _deviceId);
            cmd.Parameters.AddWithValue("$uuid", id.ToString());
            cmd.ExecuteNonQuery();
        }
    }

    public void SoftDeleteGuide(Guid id)
    {
        lock (_gate)
        {
            var now = Now();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                UPDATE aiGuideTool
                SET deletedAt = $now, updatedAt = $now, version = version + 1, lastWriterDeviceID = $dev
                WHERE uuid = $uuid AND deletedAt IS NULL;
                """;
            cmd.Parameters.AddWithValue("$now", now);
            cmd.Parameters.AddWithValue("$dev", _deviceId);
            cmd.Parameters.AddWithValue("$uuid", id.ToString());
            cmd.ExecuteNonQuery();
        }
    }

    private string Now() => _clock().ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    /// <summary>Stable per-install device id (for the sync-readiness <c>lastWriterDeviceID</c>
    /// column). Persisted in LocalSettings when packaged; a process-stable GUID otherwise.</summary>
    private static readonly string DeviceId = ResolveDeviceId();

    private static string ResolveDeviceId()
    {
        const string key = "verbinal.deviceID";
        try
        {
            var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
            if (settings.Values[key] is string existing && existing.Length > 0) return existing;
            var created = Guid.NewGuid().ToString();
            settings.Values[key] = created;
            return created;
        }
        catch
        {
            return Guid.NewGuid().ToString();
        }
    }
}
