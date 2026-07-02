using Microsoft.Data.Sqlite;

namespace CanfarDesktop.Services.Database;

/// <summary>
/// Owns the app's single SQLite connection and runs the schema migrator (via PRAGMA user_version).
/// Uses WAL for file databases and falls back to a private in-memory database if the file cannot
/// be opened (e.g. corrupt or locked), so the app keeps working rather than crashing.
/// </summary>
public sealed class AppDatabase : IDisposable
{
    public const int CurrentSchemaVersion = 3;
    private const string DbFileName = "verbinal.db";

    private readonly SqliteConnection _connection;

    /// <summary>True when the file database could not be opened and an in-memory fallback is in use.</summary>
    public bool IsInMemoryFallback { get; }

    public SqliteConnection Connection => _connection;

    /// <summary>Production constructor — opens (or creates) the database in the app's local folder.</summary>
    public AppDatabase() : this(ResolveDefaultPath(), allowFallback: true) { }

    /// <summary>
    /// Open the database at <paramref name="filePath"/> (null → private in-memory).
    /// When <paramref name="allowFallback"/> is true, a file-open failure degrades to in-memory.
    /// </summary>
    public AppDatabase(string? filePath, bool allowFallback = false)
    {
        try
        {
            _connection = Open(filePath);
        }
        catch when (allowFallback && filePath is not null)
        {
            _connection = Open(null);
            IsInMemoryFallback = true;
        }

        Migrate(_connection);
    }

    private static SqliteConnection Open(string? filePath)
    {
        var connectionString = filePath is null
            ? "Data Source=:memory:"
            : new SqliteConnectionStringBuilder { DataSource = filePath }.ToString();

        var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var pragma = connection.CreateCommand();
        pragma.CommandText = filePath is null
            ? "PRAGMA foreign_keys=ON;"
            : "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();

        return connection;
    }

    private static void Migrate(SqliteConnection connection)
    {
        using var read = connection.CreateCommand();
        read.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(read.ExecuteScalar());
        if (version >= CurrentSchemaVersion) return;

        using var tx = connection.BeginTransaction();
        if (version < 1)
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = SchemaV1;
            cmd.ExecuteNonQuery();
        }
        if (version < 2)
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = SchemaV2;
            cmd.ExecuteNonQuery();
        }
        if (version < 3)
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = SchemaV3;
            cmd.ExecuteNonQuery();
        }
        using var bump = connection.CreateCommand();
        bump.Transaction = tx;
        bump.CommandText = $"PRAGMA user_version={CurrentSchemaVersion};";
        bump.ExecuteNonQuery();
        tx.Commit();
    }

    // v1: observation notes + an external-content FTS5 index kept in sync by triggers.
    private const string SchemaV1 = """
        CREATE TABLE IF NOT EXISTS notes (
            publisherID TEXT PRIMARY KEY,
            note        TEXT NOT NULL DEFAULT '',
            rating      INTEGER NOT NULL DEFAULT 0,
            tags        TEXT NOT NULL DEFAULT '',
            updatedUtc  TEXT NOT NULL DEFAULT '',
            deleted     INTEGER NOT NULL DEFAULT 0
        );

        CREATE VIRTUAL TABLE IF NOT EXISTS noteSearch USING fts5(
            note, tags, content='notes', content_rowid='rowid'
        );

        CREATE TRIGGER IF NOT EXISTS notes_ai AFTER INSERT ON notes BEGIN
            INSERT INTO noteSearch(rowid, note, tags) VALUES (new.rowid, new.note, new.tags);
        END;

        CREATE TRIGGER IF NOT EXISTS notes_ad AFTER DELETE ON notes BEGIN
            INSERT INTO noteSearch(noteSearch, rowid, note, tags) VALUES ('delete', old.rowid, old.note, old.tags);
        END;

        CREATE TRIGGER IF NOT EXISTS notes_au AFTER UPDATE ON notes BEGIN
            INSERT INTO noteSearch(noteSearch, rowid, note, tags) VALUES ('delete', old.rowid, old.note, old.tags);
            INSERT INTO noteSearch(rowid, note, tags) VALUES (new.rowid, new.note, new.tags);
        END;
        """;

    // v2: AI Guide — per-tool description overrides + user-authored read-only guide tools.
    // Mirrors the macOS GRDB v2 schema (sparse delta overrides; soft-delete tombstones; version +
    // lastWriterDeviceID columns reserved for future sync). Guide-name uniqueness among LIVE rows is
    // enforced in AiGuideService, not by a constraint, so a deleted name can be reused.
    private const string SchemaV2 = """
        CREATE TABLE IF NOT EXISTS aiToolOverride (
            uuid               TEXT PRIMARY KEY NOT NULL,
            toolName           TEXT NOT NULL UNIQUE,
            userDescription    TEXT NOT NULL,
            createdAt          TEXT,
            updatedAt          TEXT,
            version            INTEGER NOT NULL DEFAULT 1,
            deletedAt          TEXT,
            lastWriterDeviceID TEXT
        );

        CREATE TABLE IF NOT EXISTS aiGuideTool (
            uuid               TEXT PRIMARY KEY NOT NULL,
            name               TEXT NOT NULL,
            description        TEXT NOT NULL,
            body               TEXT,
            orderIndex         INTEGER NOT NULL DEFAULT 0,
            createdAt          TEXT,
            updatedAt          TEXT,
            version            INTEGER NOT NULL DEFAULT 1,
            deletedAt          TEXT,
            lastWriterDeviceID TEXT
        );
        """;

    // v3: per-entity agent attribution — the provenance stamp (JSON) an applier leaves on notes an
    // MCP agent wrote, surfaced as the wand badge. Mirrors the macOS agentAttribution column
    // (device-local, excluded from export).
    private const string SchemaV3 = """
        ALTER TABLE notes ADD COLUMN agentAttribution TEXT;
        """;

    private static string? ResolveDefaultPath()
    {
        try
        {
            return System.IO.Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, DbFileName);
        }
        catch
        {
            return null; // unpackaged — use in-memory
        }
    }

    public void Dispose()
    {
        _connection.Dispose();
        SqliteConnection.ClearAllPools();
    }
}
