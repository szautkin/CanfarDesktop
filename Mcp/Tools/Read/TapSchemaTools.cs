using CanfarDesktop.Helpers;
using CanfarDesktop.Services;

namespace CanfarDesktop.Mcp.Tools.Read;

/// <summary>
/// <c>describe_tap_schema</c> — what the archive's tables actually contain, read from the service's
/// own <c>TAP_SCHEMA</c> rather than from a sentence in a tool description.
///
/// Without it, writing ADQL means guessing column names: the two mistakes that follow are ObsCore
/// spellings on a CAOM2 table (<c>collection_name</c> for <c>collection</c>) and a column that lives
/// on the other side of a join. The service knows both, and says so in prose.
/// </summary>
public sealed class DescribeTapSchemaTool : JsonReadTool<DescribeTapSchemaTool.Args, DescribeTapSchemaTool.Output>
{
    /// <summary>
    /// Columns returned for a single table. CADC's largest is well under this; the cap is a guard
    /// against a service answering with something enormous, not a limit anyone should reach.
    /// </summary>
    private const int MaxColumns = 500;

    /// <summary>Matches returned for a search. Enough to choose from, few enough to read.</summary>
    private const int MaxMatches = 60;

    private readonly Func<CancellationToken, Task<TapSchema>> _schema;

    public DescribeTapSchemaTool(Func<CancellationToken, Task<TapSchema>> schema) => _schema = schema;

    /// <summary>The schema is ~400 columns over three queries; a cold fetch is a second, not sixty.</summary>
    protected override TimeSpan Timeout => TimeSpan.FromSeconds(90);

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "describe_tap_schema",
        "Read the CADC TAP service's own schema: its tables, what each column means (with units and " +
        "IVOA UCDs), and the joins the service itself declares. Call this BEFORE writing ADQL rather " +
        "than guessing column names — the usual mistakes are ObsCore spellings on a CAOM2 table " +
        "(collection_name for collection) and a column that is actually on the other side of a join. " +
        "With no arguments it lists the tables; pass `table` for one table's columns, or `search` to " +
        "find a column by name, description or UCD across every table.",
        """
        {"type":"object","properties":{
          "table":{"type":"string","description":"One table's full column list (e.g. caom2.Plane)."},
          "search":{"type":"string","description":"Find columns whose name, description or UCD contains this."}
        },"additionalProperties":false}
        """);

    protected override async Task<Output> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var schema = await _schema(ct);
        if (schema.IsEmpty)
            throw new McpToolException(new BackendError("the service returned no TAP_SCHEMA tables"));

        if (!string.IsNullOrWhiteSpace(args.Table))
        {
            var name = args.Table!.Trim();
            var table = schema.Table(name)
                ?? throw new McpToolException(new UnknownTarget(
                    $"no table '{name}'. Tables: {string.Join(", ", schema.Tables.Select(t => t.Name).Take(25))}"));

            return new Output(
                Tables: [new TableView(table.Name, table.Description, table.Columns.Count,
                    table.Columns.Take(MaxColumns).Select(ColumnView.From).ToList())],
                Keys: schema.KeysTouching(table.Name).Select(KeyView.From).ToList(),
                TotalTables: schema.Tables.Count,
                Truncated: table.Columns.Count > MaxColumns);
        }

        if (!string.IsNullOrWhiteSpace(args.Search))
        {
            var needle = args.Search!.Trim();
            var matches = schema.Tables
                .SelectMany(t => t.Columns.Select(c => (Table: t, Column: c)))
                .Where(x => Contains(x.Column.Name, needle)
                         || Contains(x.Column.Description, needle)
                         || Contains(x.Column.Ucd, needle))
                .ToList();

            var grouped = matches
                .GroupBy(x => x.Table.Name, StringComparer.Ordinal)
                .Select(g =>
                {
                    var table = g.First().Table;
                    return new TableView(table.Name, table.Description, table.Columns.Count,
                        g.Take(MaxMatches).Select(x => ColumnView.From(x.Column)).ToList());
                })
                .ToList();

            return new Output(grouped, [], schema.Tables.Count, matches.Count > MaxMatches);
        }

        // The listing: every table with its description and column count, plus every declared join.
        // Columns are omitted here on purpose — ~400 of them, most irrelevant to the question asked.
        return new Output(
            Tables: schema.Tables.Select(t => new TableView(t.Name, t.Description, t.Columns.Count, null)).ToList(),
            Keys: schema.Keys.Select(KeyView.From).ToList(),
            TotalTables: schema.Tables.Count,
            Truncated: false);
    }

    private static bool Contains(string haystack, string needle)
        => !string.IsNullOrEmpty(haystack) && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    public sealed record Args
    {
        public string? Table { get; init; }
        public string? Search { get; init; }
    }

    public sealed record ColumnView(string Name, string Datatype, string? Description, string? Unit, string? Ucd)
    {
        public static ColumnView From(TapColumn c) => new(
            c.Name, c.Datatype,
            string.IsNullOrWhiteSpace(c.Description) ? null : c.Description,
            string.IsNullOrWhiteSpace(c.Unit) ? null : c.Unit,
            string.IsNullOrWhiteSpace(c.Ucd) ? null : c.Ucd);
    }

    /// <summary><paramref name="Columns"/> is null in the table LISTING and populated when one table was asked for.</summary>
    public sealed record TableView(string Name, string? Description, int ColumnCount, IReadOnlyList<ColumnView>? Columns);

    public sealed record KeyView(string FromTable, string FromColumn, string TargetTable, string TargetColumn, string? Description)
    {
        public static KeyView From(TapKey k) => new(
            k.FromTable, k.FromColumn, k.TargetTable, k.TargetColumn,
            string.IsNullOrWhiteSpace(k.Description) ? null : k.Description);
    }

    public sealed record Output(
        IReadOnlyList<TableView> Tables,
        IReadOnlyList<KeyView> Keys,
        int TotalTables,
        bool Truncated);
}

/// <summary>
/// <c>validate_adql_query</c> — check a query against the service's own schema without spending a
/// round trip on one that cannot work.
///
/// It answers only where it is confident. An empty problem list means "nothing this can be sure
/// about", not "this query is correct": a subquery, a function or an unfetched table is left alone,
/// because a false positive here talks a caller out of a query that would have worked.
/// </summary>
public sealed class ValidateAdqlQueryTool : JsonReadTool<ValidateAdqlQueryTool.Args, ValidateAdqlQueryTool.Output>
{
    private readonly Func<CancellationToken, Task<TapSchema>> _schema;

    public ValidateAdqlQueryTool(Func<CancellationToken, Task<TapSchema>> schema) => _schema = schema;

    protected override TimeSpan Timeout => TimeSpan.FromSeconds(90);

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "validate_adql_query",
        "Check an ADQL query against the archive's own schema WITHOUT running it: unknown tables, " +
        "columns a table does not have, and the ambiguous-qualifier case CADC rejects with " +
        "\"Column [obsID] is ambiguous\" (writing `Plane.obsID` where both joined tables carry obsID). " +
        "Each problem names the offending text and, where there is one obvious answer, what to write " +
        "instead. `valid:true` means nothing could be shown to be wrong — it is not a promise the " +
        "query is correct, because anything unresolvable (a subquery, a function) is deliberately left " +
        "alone rather than guessed at.",
        """{"type":"object","properties":{"adql":{"type":"string"}},"required":["adql"],"additionalProperties":false}""");

    protected override async Task<Output> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var adql = args.Adql ?? string.Empty;
        if (string.IsNullOrWhiteSpace(adql))
            throw new McpToolException(new InvalidArgument("adql is required"));

        var schema = await _schema(ct);
        var problems = AdqlValidator.Problems(adql, schema);

        return new Output(
            Valid: problems.Count == 0,
            SchemaLoaded: !schema.IsEmpty,
            Problems: problems.Select(p => new ProblemView(
                adql[p.Start..Math.Min(p.End, adql.Length)], p.Message, p.Fix, p.Start, p.End)).ToList());
    }

    public sealed record Args { public string? Adql { get; init; } }

    /// <summary><paramref name="Text"/> is the offending substring — quoting it back beats an offset a caller has to count to.</summary>
    public sealed record ProblemView(string Text, string Message, string? Fix, int Start, int End);

    public sealed record Output(bool Valid, bool SchemaLoaded, IReadOnlyList<ProblemView> Problems);
}
