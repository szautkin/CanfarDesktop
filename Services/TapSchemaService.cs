using CanfarDesktop.Models;

namespace CanfarDesktop.Services;

public interface ITapSchemaService
{
    /// <summary>The schema, fetched on first use and re-used until the cache expires.</summary>
    Task<TapSchema> GetSchemaAsync(CancellationToken cancellationToken = default);

    /// <summary>The cached schema, or null — never a fetch. See <see cref="TapSchemaService.Cached"/>.</summary>
    TapSchema? Cached();

    /// <summary>Drop the cached copy, so the next call re-reads the service.</summary>
    void Invalidate();
}

/// <summary>
/// What the TAP service's tables actually contain.
///
/// An agent writing ADQL had table names and one join, both from a sentence in a tool description,
/// and had to guess every column. The service knows: every IVOA TAP endpoint publishes
/// <c>TAP_SCHEMA</c>, and CADC's carries real prose — <c>caom2.Plane.calibrationLevel</c> describes
/// itself as "IVOA ObsCore calibration level + extensions (-1,0,1,2,3,4)", and <c>TAP_SCHEMA.keys</c>
/// says in words that <c>caom2.Plane</c> → <c>caom2.Observation</c> is the standard way to join them.
///
/// Read live rather than baked in, because the model moves: several Plane columns are marked "new in
/// 2.4", and a constant compiled last year describes last year's archive. Cached because it moves on
/// RELEASE timescales — the whole schema is around 400 columns and 34 KB, fetched in under a second,
/// and re-fetching that per tool call would be silly.
///
/// Register it as a SINGLETON. Built per call, the cache is always empty and every
/// <c>describe_tap_schema</c> re-reads 400 columns from CADC.
/// </summary>
public sealed class TapSchemaService : ITapSchemaService
{
    /// <summary>
    /// How long a fetched schema is trusted. The archive's column set changes when CADC deploys a new
    /// CAOM version, not while someone is working: an hour keeps a long session on one fetch and still
    /// picks up a deployment without a restart.
    /// </summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    /// <summary>
    /// Rows to allow from a TAP_SCHEMA query. The real answer is ~400 columns across 21 tables; this
    /// guards against a service answering with something enormous, not a limit anyone should reach.
    /// Exceeding it would silently truncate the schema, so it is generous.
    /// </summary>
    private const int MaxSchemaRows = 10_000;

    private readonly ITAPService _tap;
    private readonly object _gate = new();
    private (DateTime At, TapSchema Schema)? _cache;

    public TapSchemaService(ITAPService tap) => _tap = tap;

    public async Task<TapSchema> GetSchemaAsync(CancellationToken cancellationToken = default)
    {
        if (Cached() is { } fresh) return fresh;

        var schema = await FetchAsync(cancellationToken);

        // Last writer wins. Two callers racing the first use both fetch, which costs one extra request
        // and no correctness — cheaper than holding a lock across the network.
        lock (_gate) _cache = (DateTime.UtcNow, schema);
        return schema;
    }

    /// <summary>
    /// The cached schema, or null — never a fetch.
    ///
    /// Public so the ADQL editor can check a query as it is typed: a check that awaited a network round
    /// trip would be a check that runs on every keystroke, and one that blocked the UI thread would be
    /// worse still. Null means "not known yet", which the checker treats as "say nothing".
    /// </summary>
    public TapSchema? Cached()
    {
        lock (_gate)
        {
            if (_cache is not { } c) return null;
            return DateTime.UtcNow - c.At < CacheTtl ? c.Schema : null;
        }
    }

    public void Invalidate()
    {
        lock (_gate) _cache = null;
    }

    private async Task<TapSchema> FetchAsync(CancellationToken ct)
    {
        // Three queries rather than one join: TAP_SCHEMA is small, and a table with no columns (or a
        // service that omits `keys`) should still yield the parts that did answer.
        var tables = await QueryAsync(
            "SELECT table_name, description FROM TAP_SCHEMA.tables ORDER BY table_name", ct);
        var columns = await QueryAsync(
            "SELECT table_name, column_name, datatype, description, unit, ucd " +
            "FROM TAP_SCHEMA.columns ORDER BY table_name, column_name", ct);
        var keys = await QueryAsync(
            "SELECT k.from_table, k.target_table, kc.from_column, kc.target_column, k.description " +
            "FROM TAP_SCHEMA.keys AS k JOIN TAP_SCHEMA.key_columns AS kc ON k.key_id = kc.key_id", ct);

        return BuildSchema(tables, columns, keys);
    }

    private async Task<SearchResults> QueryAsync(string adql, CancellationToken ct)
    {
        try
        {
            return await _tap.ExecuteQueryAsync(adql, MaxSchemaRows, ct);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"TAP_SCHEMA query failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Assemble the three result sets into one schema. Free of I/O, so it can be tested against real
    /// captured rows rather than only against a live service.
    /// </summary>
    public static TapSchema BuildSchema(SearchResults tables, SearchResults columns, SearchResults keys)
    {
        var schema = new TapSchema();

        foreach (var row in tables.Rows)
            schema.Tables.Add(new TapTable(Field(row, "table_name"), Field(row, "description"), []));

        foreach (var row in columns.Rows)
        {
            var owner = Field(row, "table_name");
            var column = new TapColumn(
                Field(row, "column_name"), Field(row, "datatype"), Field(row, "description"),
                Field(row, "unit"), Field(row, "ucd"));

            var table = schema.Tables.FirstOrDefault(t => string.Equals(t.Name, owner, StringComparison.Ordinal));
            if (table is not null)
            {
                table.Columns.Add(column);
            }
            else
            {
                // A column whose table the `tables` query did not list still belongs to something;
                // dropping it would hide it entirely.
                schema.Tables.Add(new TapTable(owner, string.Empty, [column]));
            }
        }

        foreach (var row in keys.Rows)
            schema.Keys.Add(new TapKey(
                Field(row, "from_table"), Field(row, "target_table"),
                Field(row, "from_column"), Field(row, "target_column"), Field(row, "description")));

        return schema;
    }

    /// <summary>
    /// One field by NAME, exact first and case-insensitively after. Reading by name rather than by
    /// position is what stops two fields transposing the day a service returns its columns in another
    /// order; TAP services also differ on the case they echo back for TAP_SCHEMA columns. A name the
    /// response does not carry yields an empty string, so a service that omits <c>ucd</c> costs that
    /// field rather than the whole schema.
    /// </summary>
    private static string Field(SearchResultRow row, string name)
    {
        var exact = row.Get(name);
        if (!string.IsNullOrEmpty(exact)) return exact;

        foreach (var (key, value) in row.Values)
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                return value;

        return string.Empty;
    }
}
