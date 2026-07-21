using CanfarDesktop.Mcp.Tools.Write;

namespace CanfarDesktop.Mcp.Tools.Read;

// ─────────────────────────────────────────────────────────────────────────────
// Live Search-page reads: snapshots of the form, the Additional Constraints
// facets, and the results table — the read half of the search UI surface (the
// write half lives in Write/SearchUiTools.cs). Delegates are routed to the real
// page on the UI thread by AppViewStateService.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary><c>get_search_form</c> — everything currently typed into the Search form.</summary>
public sealed class GetSearchFormTool : JsonReadTool<EmptyArgs, SearchFormSnapshot>
{
    private readonly Func<Task<SearchFormSnapshot?>> _get;

    public GetSearchFormTool(Func<Task<SearchFormSnapshot?>> get) => _get = get;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_search_form",
        "Read the Search form's current state: every field of the four constraint columns (observation, " +
        "spatial + resolver status, temporal, spectral), Max Records, the staged ADQL text, and the " +
        "selected Additional Constraints facets. Pair with set_search_form / run_search.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override async Task<SearchFormSnapshot> HandleAsync(EmptyArgs args, McpToolContext context, CancellationToken ct)
        => SearchUiGuard.NotNull(await _get());
}

/// <summary>
/// <c>get_search_constraints</c> — the Additional Constraints (data train) facets: available + selected
/// values for band, collection, instrument, filter, cal. level, data type, and obs. type. Ensures the
/// data train is loaded first (cache, then a network fetch on first use), so the facets are never
/// silently empty.
/// </summary>
public sealed class GetSearchConstraintsTool : JsonReadTool<EmptyArgs, SearchFacetsSnapshot>
{
    private readonly Func<Task<SearchFacetsSnapshot?>> _get;

    public GetSearchConstraintsTool(Func<Task<SearchFacetsSnapshot?>> get) => _get = get;

    // First call may fetch the data train over the network — allow more than the default.
    protected override TimeSpan Timeout => TimeSpan.FromSeconds(90);

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_search_constraints",
        "Read the Search form's Additional Constraints facets: for each of band, collection, instrument, " +
        "filter, cal. level, data type, and obs. type — the values currently available (already narrowed " +
        "by the cascade of upstream selections) and the values selected. Loads the facet data if it " +
        "isn't yet. Use before set_search_constraints to pick valid values.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override async Task<SearchFacetsSnapshot> HandleAsync(EmptyArgs args, McpToolContext context, CancellationToken ct)
        => SearchUiGuard.NotNull(await _get());
}

/// <summary><c>get_search_results</c> — the Results tab's state, optionally with the current page's rows.</summary>
public sealed class GetSearchResultsTool : JsonReadTool<GetSearchResultsTool.Args, SearchResultsSnapshot>
{
    /// <summary>Hard cap on rows returned in one snapshot.</summary>
    public const int MaxRowsCap = 500;

    private readonly Func<bool, int, Task<SearchResultsSnapshot?>> _get;

    public GetSearchResultsTool(Func<bool, int, Task<SearchResultsSnapshot?>> get) => _get = get;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_search_results",
        "Read the search Results table: status line, total/filtered row counts, pagination, sort, active " +
        "per-column filters, the column set (keys + visibility, for set_search_results_view), and — by " +
        "default — the current page's rows (raw values, capped at 500). Page through more rows with " +
        "set_search_results_view.",
        """
        {"type":"object","properties":{
          "includeRows":{"type":"boolean","description":"Include the current page's rows (default true)"},
          "maxRows":{"type":"integer","minimum":1,"maximum":500,"description":"Cap on returned rows (default: the page size)"}
        },"additionalProperties":false}
        """);

    protected override async Task<SearchResultsSnapshot> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var maxRows = args.MaxRows is > 0 ? Math.Min(args.MaxRows.Value, MaxRowsCap) : MaxRowsCap;
        return SearchUiGuard.NotNull(await _get(args.IncludeRows ?? true, maxRows));
    }

    public sealed record Args
    {
        public bool? IncludeRows { get; init; }
        public int? MaxRows { get; init; }
    }
}
