using System.Text.Json.Serialization;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Mcp.Tools.Write;

// ─────────────────────────────────────────────────────────────────────────────
// Live Search-page steering: models + ViewState tools that drive the CADC Archive
// Search UI 1-to-1 (form fields, Additional Constraints facets, run/reset, the
// ADQL editor, the results table, exports, and the side-panel pickers). Each tool
// takes an injected delegate that AppViewStateService routes to the real page on
// the UI thread, so the tools stay pure and unit-testable — the same pattern as
// the cube/FITS viewer tools.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Full snapshot of the Search form (every field the Search Form tab shows).</summary>
public sealed class SearchFormSnapshot
{
    // Observation column
    public string ObservationId { get; init; } = string.Empty;
    public string PiName { get; init; } = string.Empty;
    public string ProposalId { get; init; } = string.Empty;
    public string ProposalTitle { get; init; } = string.Empty;
    public string Keywords { get; init; } = string.Empty;
    public string DataRelease { get; init; } = string.Empty;
    public bool PublicOnly { get; init; }
    public string Intent { get; init; } = string.Empty;

    // Spatial column
    public string Target { get; init; } = string.Empty;
    public string Resolver { get; init; } = "ALL";
    public string ResolverStatus { get; init; } = string.Empty;
    public double? ResolvedRa { get; init; }
    public double? ResolvedDec { get; init; }
    public double RadiusDeg { get; init; }
    public string PixelScale { get; init; } = string.Empty;
    public string PixelScaleUnit { get; init; } = "arcsec";
    public bool SpatialCutout { get; init; }

    // Temporal column
    public string ObservationDate { get; init; } = string.Empty;
    public string DatePreset { get; init; } = string.Empty;
    public string IntegrationTime { get; init; } = string.Empty;
    public string IntegrationTimeUnit { get; init; } = "s";
    [JsonPropertyName("timeSpan")] public string TimeSpanRange { get; init; } = string.Empty;
    public string TimeSpanUnit { get; init; } = "d";

    // Spectral column
    public string SpectralCoverage { get; init; } = string.Empty;
    public string SpectralCoverageUnit { get; init; } = "nm";
    public string SpectralSampling { get; init; } = string.Empty;
    public string SpectralSamplingUnit { get; init; } = "nm";
    public string ResolvingPower { get; init; } = string.Empty;
    public string BandpassWidth { get; init; } = string.Empty;
    public string BandpassWidthUnit { get; init; } = "nm";
    public string RestFrameEnergy { get; init; } = string.Empty;
    public string RestFrameEnergyUnit { get; init; } = "nm";
    public bool SpectralCutout { get; init; }

    // General
    public int MaxRecords { get; init; }
    public string AdqlText { get; init; } = string.Empty;
    public bool IsSearching { get; init; }

    // Additional Constraints selections (see get_search_constraints for available values)
    public IReadOnlyList<string> Bands { get; init; } = [];
    public IReadOnlyList<string> Collections { get; init; } = [];
    public IReadOnlyList<string> Instruments { get; init; } = [];
    public IReadOnlyList<string> Filters { get; init; } = [];
    public IReadOnlyList<string> CalLevels { get; init; } = [];
    public IReadOnlyList<string> DataTypes { get; init; } = [];
    public IReadOnlyList<string> ObsTypes { get; init; } = [];
}

/// <summary>Patch for <c>set_search_form</c> — only non-null fields are applied.</summary>
public sealed class SearchFormPatch
{
    public string? ObservationId { get; init; }
    public string? PiName { get; init; }
    public string? ProposalId { get; init; }
    public string? ProposalTitle { get; init; }
    public string? Keywords { get; init; }
    public string? DataRelease { get; init; }
    public bool? PublicOnly { get; init; }
    public string? Intent { get; init; }
    public string? Target { get; init; }
    public string? Resolver { get; init; }
    public double? RadiusDeg { get; init; }
    public string? PixelScale { get; init; }
    public string? PixelScaleUnit { get; init; }
    public bool? SpatialCutout { get; init; }
    public string? ObservationDate { get; init; }
    public string? DatePreset { get; init; }
    public string? IntegrationTime { get; init; }
    public string? IntegrationTimeUnit { get; init; }
    [JsonPropertyName("timeSpan")] public string? TimeSpanRange { get; init; }
    public string? TimeSpanUnit { get; init; }
    public string? SpectralCoverage { get; init; }
    public string? SpectralCoverageUnit { get; init; }
    public string? SpectralSampling { get; init; }
    public string? SpectralSamplingUnit { get; init; }
    public string? ResolvingPower { get; init; }
    public string? BandpassWidth { get; init; }
    public string? BandpassWidthUnit { get; init; }
    public string? RestFrameEnergy { get; init; }
    public string? RestFrameEnergyUnit { get; init; }
    public bool? SpectralCutout { get; init; }
    public int? MaxRecords { get; init; }
}

/// <summary>One Additional-Constraints facet: cascade-filtered available values + current selection.</summary>
public sealed record SearchFacetView(IReadOnlyList<string> Available, IReadOnlyList<string> Selected);

/// <summary>The Additional Constraints (data train) state — the seven cascading facet lists.</summary>
public sealed class SearchFacetsSnapshot
{
    public bool Loaded { get; init; }
    public int RowCount { get; init; }
    public SearchFacetView Bands { get; init; } = new([], []);
    public SearchFacetView Collections { get; init; } = new([], []);
    public SearchFacetView Instruments { get; init; } = new([], []);
    public SearchFacetView Filters { get; init; } = new([], []);
    public SearchFacetView CalLevels { get; init; } = new([], []);
    public SearchFacetView DataTypes { get; init; } = new([], []);
    public SearchFacetView ObsTypes { get; init; } = new([], []);
}

/// <summary>Args for <c>set_search_constraints</c> — provided facets REPLACE that facet's selection.</summary>
public sealed class SearchFacetSelections
{
    public IReadOnlyList<string>? Bands { get; init; }
    public IReadOnlyList<string>? Collections { get; init; }
    public IReadOnlyList<string>? Instruments { get; init; }
    public IReadOnlyList<string>? Filters { get; init; }
    public IReadOnlyList<string>? CalLevels { get; init; }
    public IReadOnlyList<string>? DataTypes { get; init; }
    public IReadOnlyList<string>? ObsTypes { get; init; }
    public bool ClearAll { get; init; }
}

/// <summary>Result of a constraints write: values the cascade dropped, plus the resulting facet state.</summary>
public sealed record SearchConstraintsOutcome(bool Applied, IReadOnlyList<string> Dropped, SearchFacetsSnapshot Facets);

/// <summary>Outcome of running a search (form or ADQL): the ADQL run, row count, and the UI status line.</summary>
public sealed record SearchRunOutcome(bool Ran, string? Adql, int TotalRows, string? Status, string? Error);

/// <summary>Outcome of staging ADQL text into the editor without running it.</summary>
public sealed record AdqlStageOutcome(bool Applied, string Adql);

/// <summary>One results-table column: key (for sort/filter/visibility), display label, visibility.</summary>
public sealed record SearchResultColumnView(string Key, string Label, bool Visible);

/// <summary>Snapshot of the Results tab: status, pagination, sort/filter state, columns, current page rows.</summary>
public sealed class SearchResultsSnapshot
{
    public bool HasResults { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? Adql { get; init; }
    public int TotalRows { get; init; }
    public int FilteredRows { get; init; }
    public int CurrentPage { get; init; }
    public int TotalPages { get; init; }
    public int RowsPerPage { get; init; }
    public string PageStatus { get; init; } = string.Empty;
    public string? SortColumn { get; init; }
    public bool SortAscending { get; init; }
    public IReadOnlyDictionary<string, string> Filters { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<SearchResultColumnView> Columns { get; init; } = [];
    /// <summary>Original TAP headers for <see cref="Rows"/> cells, in cell order (null when rows omitted).</summary>
    public IReadOnlyList<string>? RowColumns { get; init; }
    /// <summary>Current-page rows (raw cell values aligned to <see cref="RowColumns"/>; null when omitted).</summary>
    public IReadOnlyList<IReadOnlyList<string>>? Rows { get; init; }
}

/// <summary>Args for <c>set_search_results_view</c> — apply any subset of results-table changes at once.</summary>
public sealed class SearchResultsCommand
{
    public int? Page { get; init; }
    /// <summary>"first" | "prev" | "next" | "last" (alternative to an absolute page).</summary>
    public string? PageAction { get; init; }
    public int? RowsPerPage { get; init; }
    public string? SortColumn { get; init; }
    /// <summary>With sortColumn: explicit direction; omitted = ascending (repeat with false to flip).</summary>
    public bool? SortAscending { get; init; }
    /// <summary>Column key → filter text. An empty value clears that column's filter.</summary>
    public Dictionary<string, string>? SetFilters { get; init; }
    public bool ClearFilters { get; init; }
    public IReadOnlyList<string>? ShowColumns { get; init; }
    public IReadOnlyList<string>? HideColumns { get; init; }
    /// <summary>Column key → display unit id (empty value = column default). Same menu as the header dropdown.</summary>
    public Dictionary<string, string>? ColumnUnits { get; init; }
    /// <summary>Convert the active filters to ADQL WHERE clauses and stage into the ADQL editor.</summary>
    public bool ApplyFiltersToAdql { get; init; }
}

/// <summary>Outcome of exporting the results table to a local CSV/TSV file.</summary>
public sealed record SearchExportOutcome(bool Exported, string? Path, int Rows, string? Error);

/// <summary>Outcome of loading a recent search back into the form.</summary>
public sealed record LoadRecentSearchOutcome(bool Loaded, string? Summary, string? Error, SearchFormSnapshot? Form);

/// <summary>Shared validation for the search UI tools.</summary>
internal static class SearchUiGuard
{
    public static readonly string[] Intents = ["", "science", "calibration"];
    public static readonly string[] Resolvers = ["ALL", "SIMBAD", "NED", "VIZIER", "NONE"];
    public static readonly string[] DatePresets = ["", "Last24h", "LastWeek", "LastMonth"];
    public static readonly int[] RowsPerPageOptions = [25, 50, 100, 250, 500];
    public static readonly string[] PageActions = ["first", "prev", "next", "last"];

    public static void Choice(string? value, string[] allowed, string field)
    {
        if (value is not null && !allowed.Contains(value, StringComparer.OrdinalIgnoreCase))
            throw new McpToolException(new InvalidArgument(
                $"{field} must be one of: {string.Join(", ", allowed.Where(a => a.Length > 0))}"));
    }

    public static T NotNull<T>(T? snapshot) where T : class
        => snapshot ?? throw new McpToolException(new BackendError(
            "the Search page is unavailable (app UI not ready); retry shortly"));

    public static void ValidatePatch(SearchFormPatch p)
    {
        Choice(p.Intent, Intents, "intent");
        Choice(p.Resolver, Resolvers, "resolver");
        Choice(p.DatePreset, DatePresets, "datePreset");
        Choice(p.PixelScaleUnit, UnitConverter.PixelScaleUnits, "pixelScaleUnit");
        Choice(p.IntegrationTimeUnit, UnitConverter.TimeUnits, "integrationTimeUnit");
        Choice(p.TimeSpanUnit, UnitConverter.TimeUnits, "timeSpanUnit");
        Choice(p.SpectralCoverageUnit, UnitConverter.SpectralUnits, "spectralCoverageUnit");
        Choice(p.SpectralSamplingUnit, UnitConverter.SpectralUnits, "spectralSamplingUnit");
        Choice(p.BandpassWidthUnit, UnitConverter.SpectralUnits, "bandpassWidthUnit");
        Choice(p.RestFrameEnergyUnit, UnitConverter.SpectralUnits, "restFrameEnergyUnit");
        if (p.RadiusDeg is < 0 or > 90)
            throw new McpToolException(new InvalidArgument("radiusDeg must be in [0, 90]"));
        if (p.MaxRecords is < 1 or > 30000)
            throw new McpToolException(new InvalidArgument("maxRecords must be in [1, 30000]"));
    }
}

/// <summary>
/// <c>set_search_form</c> — fill any subset of the Search form's fields (all four constraint columns +
/// Max Records) exactly as typing into the UI would, and bring the form into view. Setting
/// <c>target</c> triggers the same debounced name resolution as typing it. Verb class ViewState:
/// live-applied, no proposal. Use set_search_constraints for the Additional Constraints facets.
/// </summary>
public sealed class SetSearchFormTool : JsonReadTool<SearchFormPatch, SearchFormSnapshot>
{
    private readonly Func<SearchFormPatch, Task<SearchFormSnapshot?>> _apply;

    public SetSearchFormTool(Func<SearchFormPatch, Task<SearchFormSnapshot?>> apply) => _apply = apply;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "set_search_form",
        "Fill Search form fields (only the fields you pass change) and bring the form into view — the UI " +
        "equivalent of typing into the CADC Archive Search form. Setting `target` auto-resolves it to " +
        "RA/Dec (check `resolverStatus` in the returned snapshot). Range fields accept the UI's range " +
        "syntax (e.g. \"2020..2021\", \"> 2019\"). Facet lists (band/collection/…) are set via " +
        "set_search_constraints; run the query with run_search. Live-applied (no proposal).",
        """
        {"type":"object","properties":{
          "observationId":{"type":"string","description":"Observation ID pattern (e.g. jw01345*)"},
          "piName":{"type":"string"},"proposalId":{"type":"string"},"proposalTitle":{"type":"string"},
          "keywords":{"type":"string"},"dataRelease":{"type":"string","description":"e.g. > 2023-01-01"},
          "publicOnly":{"type":"boolean"},
          "intent":{"type":"string","enum":["","science","calibration"]},
          "target":{"type":"string","description":"Target name or coordinates (e.g. M31)"},
          "resolver":{"type":"string","enum":["ALL","SIMBAD","NED","VIZIER","NONE"]},
          "radiusDeg":{"type":"number","minimum":0,"maximum":90},
          "pixelScale":{"type":"string","description":"e.g. 0.1..1.0"},
          "pixelScaleUnit":{"type":"string","enum":["arcsec","arcmin","deg"]},
          "spatialCutout":{"type":"boolean"},
          "observationDate":{"type":"string","description":"e.g. 2020..2021"},
          "datePreset":{"type":"string","enum":["","Last24h","LastWeek","LastMonth"]},
          "integrationTime":{"type":"string","description":"e.g. 100..3600"},
          "integrationTimeUnit":{"type":"string","enum":["s","m","h","d","y"]},
          "timeSpan":{"type":"string","description":"e.g. 1..10"},
          "timeSpanUnit":{"type":"string","enum":["s","m","h","d","y"]},
          "spectralCoverage":{"type":"string","description":"e.g. 400..700"},
          "spectralCoverageUnit":{"type":"string"},
          "spectralSampling":{"type":"string"},"spectralSamplingUnit":{"type":"string"},
          "resolvingPower":{"type":"string","description":"e.g. 1000..5000"},
          "bandpassWidth":{"type":"string"},"bandpassWidthUnit":{"type":"string"},
          "restFrameEnergy":{"type":"string"},"restFrameEnergyUnit":{"type":"string"},
          "spectralCutout":{"type":"boolean"},
          "maxRecords":{"type":"integer","minimum":1,"maximum":30000}
        },"additionalProperties":false}
        """);

    protected override async Task<SearchFormSnapshot> HandleAsync(SearchFormPatch args, McpToolContext context, CancellationToken ct)
    {
        SearchUiGuard.ValidatePatch(args);
        return SearchUiGuard.NotNull(await _apply(args));
    }
}

/// <summary>
/// <c>set_search_constraints</c> — set the Additional Constraints facet selections (band, collection,
/// instrument, filter, cal. level, data type, obs. type). Loads the data train first if needed, applies
/// the same cascade the UI applies (upstream picks narrow downstream availability; invalid downstream
/// picks are dropped and reported), and expands the Additional Constraints panel so the user sees the
/// change. Verb class ViewState: live-applied, no proposal.
/// </summary>
public sealed class SetSearchConstraintsTool : JsonReadTool<SearchFacetSelections, SearchConstraintsOutcome>
{
    private readonly Func<SearchFacetSelections, Task<SearchConstraintsOutcome?>> _apply;

    public SetSearchConstraintsTool(Func<SearchFacetSelections, Task<SearchConstraintsOutcome?>> apply) => _apply = apply;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    // The data train may need a network fetch on first use — allow more than the UI-dispatch default.
    protected override TimeSpan Timeout => TimeSpan.FromSeconds(90);

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "set_search_constraints",
        "Set the Search form's Additional Constraints facets. Each facet you pass REPLACES that facet's " +
        "selection (pass [] to clear one; clearAll:true to clear everything). Facets cascade top-down " +
        "(band → collection → instrument → filter → cal. level → data type → obs. type): values invalid " +
        "under the cascade are dropped and returned in `dropped` — check it. Call get_search_constraints " +
        "first to see available values. Live-applied (no proposal).",
        """
        {"type":"object","properties":{
          "bands":{"type":"array","items":{"type":"string"}},
          "collections":{"type":"array","items":{"type":"string"}},
          "instruments":{"type":"array","items":{"type":"string"}},
          "filters":{"type":"array","items":{"type":"string"}},
          "calLevels":{"type":"array","items":{"type":"string"}},
          "dataTypes":{"type":"array","items":{"type":"string"}},
          "obsTypes":{"type":"array","items":{"type":"string"}},
          "clearAll":{"type":"boolean"}
        },"additionalProperties":false}
        """);

    protected override async Task<SearchConstraintsOutcome> HandleAsync(SearchFacetSelections args, McpToolContext context, CancellationToken ct)
    {
        if (!args.ClearAll && args.Bands is null && args.Collections is null && args.Instruments is null
            && args.Filters is null && args.CalLevels is null && args.DataTypes is null && args.ObsTypes is null)
            throw new McpToolException(new InvalidArgument("pass at least one facet array, or clearAll:true"));

        return SearchUiGuard.NotNull(await _apply(args));
    }
}

/// <summary>
/// <c>reset_search_form</c> — the Search form's Reset button: clear every field and facet selection.
/// Verb class ViewState: live-applied (only clears the in-progress form, not saved state).
/// </summary>
public sealed class ResetSearchFormTool : JsonReadTool<EmptyArgs, SearchFormSnapshot>
{
    private readonly Func<Task<SearchFormSnapshot?>> _reset;

    public ResetSearchFormTool(Func<Task<SearchFormSnapshot?>> reset) => _reset = reset;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "reset_search_form",
        "Reset the Search form: clear every field and Additional Constraints selection (the form's Reset " +
        "button). Does not touch saved queries or search history. Live-applied (no proposal).",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override async Task<SearchFormSnapshot> HandleAsync(EmptyArgs args, McpToolContext context, CancellationToken ct)
        => SearchUiGuard.NotNull(await _reset());
}

/// <summary>
/// <c>run_search</c> — the Search button: build ADQL from the current form (including facets), execute
/// it, show the Results tab, and record the search in Recent Searches. Verb class ViewState.
/// </summary>
public sealed class RunSearchTool : JsonReadTool<EmptyArgs, SearchRunOutcome>
{
    private readonly Func<Task<SearchRunOutcome>> _run;

    public RunSearchTool(Func<Task<SearchRunOutcome>> run) => _run = run;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "run_search",
        "Click Search: build ADQL from the current Search form (set_search_form / set_search_constraints " +
        "first), run it, show the Results tab, and record it in Recent Searches. Returns the ADQL and row " +
        "count. For a headless query that doesn't touch the user's UI, use search_observations instead. " +
        "Live-applied (no proposal).",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override Task<SearchRunOutcome> HandleAsync(EmptyArgs args, McpToolContext context, CancellationToken ct)
        => _run();
}

/// <summary>
/// <c>set_adql_query</c> — stage ADQL text into the ADQL Editor tab WITHOUT running it (so the user can
/// review/edit). Verb class ViewState.
/// </summary>
public sealed class SetAdqlQueryTool : JsonReadTool<SetAdqlQueryTool.Args, AdqlStageOutcome>
{
    private readonly Func<string, Task<AdqlStageOutcome?>> _apply;

    public SetAdqlQueryTool(Func<string, Task<AdqlStageOutcome?>> apply) => _apply = apply;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "set_adql_query",
        "Put ADQL text into the Search page's ADQL Editor tab and show it, WITHOUT executing — the user " +
        "can review and edit first. Use execute_adql_query to run it. Live-applied (no proposal).",
        """{"type":"object","properties":{"adql":{"type":"string"}},"required":["adql"],"additionalProperties":false}""");

    protected override async Task<AdqlStageOutcome> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args.Adql))
            throw new McpToolException(new InvalidArgument("adql is required"));
        return SearchUiGuard.NotNull(await _apply(args.Adql.Trim()));
    }

    public sealed record Args { public string? Adql { get; init; } }
}

/// <summary>
/// <c>execute_adql_query</c> — the ADQL Editor's Execute button: run the editor's current ADQL (or the
/// ADQL passed in, which is staged first) and show the Results tab. Verb class ViewState.
/// </summary>
public sealed class ExecuteAdqlQueryTool : JsonReadTool<ExecuteAdqlQueryTool.Args, SearchRunOutcome>
{
    private readonly Func<string?, Task<SearchRunOutcome>> _execute;

    public ExecuteAdqlQueryTool(Func<string?, Task<SearchRunOutcome>> execute) => _execute = execute;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "execute_adql_query",
        "Execute ADQL in the Search page's ADQL Editor and show the Results tab. Pass `adql` to stage and " +
        "run it, or omit it to run whatever is already in the editor. For a headless query that doesn't " +
        "touch the user's UI, use search_observations instead. Live-applied (no proposal).",
        """{"type":"object","properties":{"adql":{"type":"string"}},"additionalProperties":false}""");

    protected override Task<SearchRunOutcome> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
        => _execute(string.IsNullOrWhiteSpace(args.Adql) ? null : args.Adql.Trim());

    public sealed record Args { public string? Adql { get; init; } }
}

/// <summary>
/// <c>set_search_results_view</c> — drive the Results tab exactly as the user can: pagination, rows per
/// page, column sort, per-column filters, column visibility, display units, and "Apply to ADQL".
/// Verb class ViewState.
/// </summary>
public sealed class SetSearchResultsViewTool : JsonReadTool<SearchResultsCommand, SearchResultsSnapshot>
{
    private readonly Func<SearchResultsCommand, Task<SearchResultsSnapshot?>> _apply;

    public SetSearchResultsViewTool(Func<SearchResultsCommand, Task<SearchResultsSnapshot?>> apply) => _apply = apply;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "set_search_results_view",
        "Steer the search Results table (after run_search / execute_adql_query): go to a page, change " +
        "rows per page, sort by a column, set/clear per-column text filters, show/hide columns, change a " +
        "column's display unit, or convert the active filters to ADQL (applyFiltersToAdql stages the " +
        "filtered query into the ADQL editor). Column keys come from get_search_results. Live-applied " +
        "(no proposal).",
        """
        {"type":"object","properties":{
          "page":{"type":"integer","minimum":1},
          "pageAction":{"type":"string","enum":["first","prev","next","last"]},
          "rowsPerPage":{"type":"integer","enum":[25,50,100,250,500]},
          "sortColumn":{"type":"string","description":"Column key to sort by"},
          "sortAscending":{"type":"boolean","description":"With sortColumn; default true"},
          "setFilters":{"type":"object","additionalProperties":{"type":"string"},"description":"Column key -> filter text ('' clears that column)"},
          "clearFilters":{"type":"boolean"},
          "showColumns":{"type":"array","items":{"type":"string"}},
          "hideColumns":{"type":"array","items":{"type":"string"}},
          "columnUnits":{"type":"object","additionalProperties":{"type":"string"},"description":"Column key -> unit id ('' = column default)"},
          "applyFiltersToAdql":{"type":"boolean"}
        },"additionalProperties":false}
        """);

    protected override async Task<SearchResultsSnapshot> HandleAsync(SearchResultsCommand args, McpToolContext context, CancellationToken ct)
    {
        if (args.PageAction is not null)
            SearchUiGuard.Choice(args.PageAction, SearchUiGuard.PageActions, "pageAction");
        if (args.Page is < 1)
            throw new McpToolException(new InvalidArgument("page must be >= 1"));
        if (args.RowsPerPage is { } rpp && !SearchUiGuard.RowsPerPageOptions.Contains(rpp))
            throw new McpToolException(new InvalidArgument(
                $"rowsPerPage must be one of: {string.Join(", ", SearchUiGuard.RowsPerPageOptions)}"));

        var hasDirective = args.Page is not null || args.PageAction is not null || args.RowsPerPage is not null
            || args.SortColumn is not null || args.SetFilters is { Count: > 0 } || args.ClearFilters
            || args.ShowColumns is { Count: > 0 } || args.HideColumns is { Count: > 0 }
            || args.ColumnUnits is { Count: > 0 } || args.ApplyFiltersToAdql;
        if (!hasDirective)
            throw new McpToolException(new InvalidArgument(
                "pass at least one change (page, sort, filters, columns, units, or applyFiltersToAdql); " +
                "use get_search_results to read the current state"));

        return SearchUiGuard.NotNull(await _apply(args));
    }
}

/// <summary>
/// <c>export_search_results</c> — the Results toolbar's CSV/TSV export, minus the file picker: write the
/// full result set to a local file (default: Downloads\Verbinal). Verb class ViewState (live file
/// export, same class as export_cube_figure).
/// </summary>
public sealed class ExportSearchResultsTool : JsonReadTool<ExportSearchResultsTool.Args, SearchExportOutcome>
{
    private readonly Func<string, string?, Task<SearchExportOutcome>> _export;

    public ExportSearchResultsTool(Func<string, string?, Task<SearchExportOutcome>> export) => _export = export;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "export_search_results",
        "Export the current search results to a local CSV or TSV file (all rows, not just the visible " +
        "page). Default destination is a timestamped file in Downloads\\Verbinal; pass `path` to choose. " +
        "Run a search first. Live-applied (no proposal).",
        """
        {"type":"object","properties":{
          "format":{"type":"string","enum":["csv","tsv"]},
          "path":{"type":"string","description":"Optional absolute destination file path"}
        },"required":["format"],"additionalProperties":false}
        """);

    protected override async Task<SearchExportOutcome> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var format = (args.Format ?? string.Empty).Trim().ToLowerInvariant();
        if (format is not ("csv" or "tsv"))
            throw new McpToolException(new InvalidArgument("format must be 'csv' or 'tsv'"));
        return await _export(format, string.IsNullOrWhiteSpace(args.Path) ? null : args.Path.Trim());
    }

    public sealed record Args
    {
        public string? Format { get; init; }
        public string? Path { get; init; }
    }
}

/// <summary>
/// <c>load_recent_search</c> — the Recent Searches panel's "load into form" button: restore a past
/// search's form state (and its ADQL) by index from list_recent_searches. Verb class ViewState.
/// </summary>
public sealed class LoadRecentSearchTool : JsonReadTool<LoadRecentSearchTool.Args, LoadRecentSearchOutcome>
{
    private readonly Func<int, Task<LoadRecentSearchOutcome?>> _load;

    public LoadRecentSearchTool(Func<int, Task<LoadRecentSearchOutcome?>> load) => _load = load;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "load_recent_search",
        "Load a recent search back into the Search form (form fields, facets, and ADQL) and show it — " +
        "the Recent Searches panel's load button. `index` is 0-based, newest first, matching " +
        "list_recent_searches order. Live-applied (no proposal).",
        """{"type":"object","properties":{"index":{"type":"integer","minimum":0}},"required":["index"],"additionalProperties":false}""");

    protected override async Task<LoadRecentSearchOutcome> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (args.Index is null or < 0)
            throw new McpToolException(new InvalidArgument("index (>= 0) is required"));
        return SearchUiGuard.NotNull(await _load(args.Index.Value));
    }

    public sealed record Args { public int? Index { get; init; } }
}

/// <summary>
/// <c>run_saved_query</c> — the Saved Queries panel's Run button: execute a saved query by name in the
/// UI and show the Results tab. Verb class ViewState.
/// </summary>
public sealed class RunSavedQueryTool : JsonReadTool<RunSavedQueryTool.Args, SearchRunOutcome>
{
    private readonly Func<string, Task<SearchRunOutcome>> _run;

    public RunSavedQueryTool(Func<string, Task<SearchRunOutcome>> run) => _run = run;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "run_saved_query",
        "Run one of the user's saved ADQL queries by exact name in the Search UI and show the Results " +
        "tab (the Saved Queries panel's Run button). Names come from list_saved_queries. Live-applied " +
        "(no proposal).",
        """{"type":"object","properties":{"name":{"type":"string"}},"required":["name"],"additionalProperties":false}""");

    protected override Task<SearchRunOutcome> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args.Name))
            throw new McpToolException(new InvalidArgument("name is required"));
        return _run(args.Name.Trim());
    }

    public sealed record Args { public string? Name { get; init; } }
}
