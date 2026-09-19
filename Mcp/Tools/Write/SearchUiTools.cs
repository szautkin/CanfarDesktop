using System.Text.Json;

namespace CanfarDesktop.Mcp.Tools.Write;

// The search_* UI tools: read and drive the live Search page — its form, its facets, the query it
// runs, and the results grid it shows.
//
// All of them are McpVerbClass.ViewState: they change what the user is looking at, not what is
// stored, so they are live-applied rather than proposed. Each takes the one delegate it needs (never
// the whole ISearchUiBridge), so a test supplies a lambda.
//
// search_observations (in SearchExecTools) remains the headless way to run a query and get rows
// back. These tools are for the case where the point is that the USER sees the result.

/// <summary>
/// The vocabulary the search tools accept, and the conversions from JSON to the page's own types.
///
/// The field and facet name lists are the ones the tool SCHEMAS advertise. The page holds the table
/// that actually applies them (<c>SearchPage.Mcp.cs</c>) and checks itself against these lists, so a
/// name can never be advertised to an agent without something behind it.
/// </summary>
public static class SearchToolArgs
{
    /// <summary>Every field <c>set_search_form</c> accepts, in the order the form shows them.</summary>
    public static readonly string[] FormFields =
    [
        "observationId", "proposalPi", "proposalId", "proposalTitle", "proposalKeywords", "intent", "publicOnly",
        "target", "resolverService", "searchRadius", "pixelScale", "pixelScaleUnit", "spatialCutout",
        "observationDate", "datePreset", "dateStart", "dateEnd", "integrationTimeMin", "integrationTimeMax",
        "integrationTimeUnit", "timeSpan", "timeSpanUnit", "dataRelease",
        "wavelengthMin", "wavelengthMax", "spectralCoverage", "spectralCoverageUnit", "spectralSampling",
        "spectralSamplingUnit", "resolvingPower", "bandpassWidth", "bandpassWidthUnit", "restFrameEnergy",
        "restFrameEnergyUnit", "spectralCutout", "maxRecords",
    ];

    /// <summary>Every facet <c>set_search_constraints</c> accepts.</summary>
    public static readonly string[] Facets =
    [
        "bands", "collections", "instruments", "filters", "calibrationLevels", "dataProductTypes", "observationTypes",
    ];

    /// <summary>
    /// A form value as the page stores it — text. A number or a boolean is accepted and written in its
    /// invariant form (an agent that sends <c>0.5</c> rather than <c>"0.5"</c> means the same thing);
    /// an explicit null clears the field. An object or an array is a mistake worth naming.
    /// </summary>
    public static string? ToFormValue(string name, JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => throw new McpToolException(new InvalidArgument(
            $"'{name}' takes a string, number, boolean or null — got {value.ValueKind.ToString().ToLowerInvariant()}")),
    };

    /// <summary>A facet selection: an array of strings, or one string as shorthand for a single value.</summary>
    public static IReadOnlyList<string> ToFacetValues(string name, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
            return [value.GetString() ?? string.Empty];

        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return Array.Empty<string>();

        if (value.ValueKind != JsonValueKind.Array)
            throw new McpToolException(new InvalidArgument(
                $"facet '{name}' takes an array of strings — got {value.ValueKind.ToString().ToLowerInvariant()}"));

        var list = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                throw new McpToolException(new InvalidArgument($"facet '{name}' takes strings — got {item.ValueKind.ToString().ToLowerInvariant()}"));
            var s = item.GetString();
            if (!string.IsNullOrWhiteSpace(s)) list.Add(s!);
        }
        return list;
    }

    /// <summary>Build a JSON-Schema property map from a name list, all of them optional.</summary>
    public static string SchemaFor(IEnumerable<string> names, string valueSchema) =>
        string.Join(",", names.Select(n => $"\"{n}\":{valueSchema}"));

    /// <summary>Values close enough to be worth suggesting when one was rejected. Capped so a reply stays readable.</summary>
    public static IReadOnlyList<string> DidYouMean(string value, IReadOnlyList<string> available, int cap = 5)
    {
        if (available.Count == 0) return Array.Empty<string>();
        var near = available
            .Where(a => a.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                        value.Contains(a, StringComparison.OrdinalIgnoreCase))
            .Take(cap)
            .ToList();
        return near.Count > 0 ? near : available.Take(cap).ToList();
    }
}

/// <summary>Args shaped as "any of these named fields" — the tool validates the names, not the schema alone.</summary>
public sealed class NamedFieldArgs : Dictionary<string, JsonElement>;

// ── Form ────────────────────────────────────────────────────────────────────────────────────────

/// <summary><c>get_search_form</c> — every field of the Search form as it currently stands.</summary>
public sealed class GetSearchFormTool : JsonReadTool<EmptyArgs, SearchFormView>
{
    private readonly Func<Task<SearchFormView>> _get;

    public GetSearchFormTool(Func<Task<SearchFormView>> get) => _get = get;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_search_form",
        "Read the live Search form: every constraint field with its value (and its own unit, where the " +
        "field carries one), the resolved sky position, the record limit, the ADQL the form would run, " +
        "and which tab is showing. Use before set_search_form so you change one field rather than " +
        "replacing a form the user has been filling in.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override Task<SearchFormView> HandleAsync(EmptyArgs args, McpToolContext context, CancellationToken ct) => _get();
}

/// <summary><c>set_search_form</c> — set one or more form fields. An unknown field applies nothing.</summary>
public sealed class SetSearchFormTool : JsonReadTool<NamedFieldArgs, SearchFormApplied>
{
    private readonly Func<SearchFormPatch, Task<SearchFormApplied>> _set;

    public SetSearchFormTool(Func<SearchFormPatch, Task<SearchFormApplied>> set) => _set = set;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "set_search_form",
        "Set one or more fields on the live Search form (the user sees the change; nothing runs until " +
        "run_search). Send only the fields you are changing. A field name the form does not have " +
        "applies NOTHING and comes back named, with the vocabulary that would have worked — a partly " +
        "applied form is a state neither of us can reason about. Values may be strings, numbers or " +
        "booleans; null clears a field.",
        $$"""
        {"type":"object","properties":{
          {{SearchToolArgs.SchemaFor(SearchToolArgs.FormFields, """{"type":["string","number","boolean","null"]}""")}}
        },"additionalProperties":false}
        """);

    protected override async Task<SearchFormApplied> HandleAsync(NamedFieldArgs args, McpToolContext context, CancellationToken ct)
    {
        if (args.Count == 0)
            throw new McpToolException(new InvalidArgument(
                $"give at least one field to set. Known fields: {string.Join(", ", SearchToolArgs.FormFields)}"));

        var patch = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in args)
            patch[name] = SearchToolArgs.ToFormValue(name, value);

        var applied = await _set(new SearchFormPatch(patch));

        if (applied.Unknown.Count > 0)
            throw new McpToolException(new InvalidArgument(
                $"no such form field: {string.Join(", ", applied.Unknown)}. " +
                $"Known fields: {string.Join(", ", applied.KnownFields)}"));

        return applied;
    }
}

// ── Constraints ─────────────────────────────────────────────────────────────────────────────────

/// <summary><c>get_search_constraints</c> — the faceted data train: what each facet offers, and what is selected.</summary>
public sealed class GetSearchConstraintsTool : JsonReadTool<EmptyArgs, SearchConstraintsView>
{
    private readonly Func<Task<SearchConstraintsView>> _get;

    public GetSearchConstraintsTool(Func<Task<SearchConstraintsView>> get) => _get = get;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_search_constraints",
        "Read the Search page's faceted data train: for each facet (bands, collections, instruments, " +
        "filters, calibration levels, data product types, observation types) the values it currently " +
        "offers and the values selected. The facets CASCADE — what one offers depends on what the " +
        "others have selected — so read this again after every set_search_constraints.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override Task<SearchConstraintsView> HandleAsync(EmptyArgs args, McpToolContext context, CancellationToken ct) => _get();
}

/// <summary><c>set_search_constraints</c> — replace the selection in one or more facets.</summary>
public sealed class SetSearchConstraintsTool : JsonReadTool<NamedFieldArgs, SearchConstraintsApplied>
{
    private readonly Func<SearchConstraintsPatch, Task<SearchConstraintsApplied>> _set;

    public SetSearchConstraintsTool(Func<SearchConstraintsPatch, Task<SearchConstraintsApplied>> set) => _set = set;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "set_search_constraints",
        "Select values in the Search page's facets. Each facet you name is REPLACED by the values you " +
        "give (send an empty array to clear one); facets you omit are untouched. A value the facet does " +
        "not offer applies nothing and comes back with the values that were available — check " +
        "get_search_constraints first, since the facets cascade.",
        $$"""
        {"type":"object","properties":{
          {{SearchToolArgs.SchemaFor(SearchToolArgs.Facets, """{"type":["array","string","null"],"items":{"type":"string"}}""")}}
        },"additionalProperties":false}
        """);

    protected override async Task<SearchConstraintsApplied> HandleAsync(NamedFieldArgs args, McpToolContext context, CancellationToken ct)
    {
        if (args.Count == 0)
            throw new McpToolException(new InvalidArgument(
                $"give at least one facet to set. Known facets: {string.Join(", ", SearchToolArgs.Facets)}"));

        var patch = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in args)
            patch[name] = SearchToolArgs.ToFacetValues(name, value);

        var applied = await _set(new SearchConstraintsPatch(patch));

        if (applied.Unknown.Count > 0)
            throw new McpToolException(new InvalidArgument(
                $"no such facet: {string.Join(", ", applied.Unknown)}. " +
                $"Known facets: {string.Join(", ", applied.KnownFacets)}"));

        if (applied.Rejected.Count > 0)
        {
            var detail = string.Join("; ", applied.Rejected.Select(r =>
                $"{r.Facet} does not offer '{r.Value}'" +
                (r.DidYouMean.Count > 0 ? $" (available: {string.Join(", ", r.DidYouMean)})" : "")));
            throw new McpToolException(new InvalidArgument(detail));
        }

        return applied;
    }
}

// ── Running ─────────────────────────────────────────────────────────────────────────────────────

/// <summary><c>run_search</c> — run the form as the Execute button would, and show the results.</summary>
public sealed class RunSearchTool : JsonReadTool<EmptyArgs, SearchRunOutcome>
{
    private readonly Func<Task<SearchRunOutcome>> _run;

    public RunSearchTool(Func<Task<SearchRunOutcome>> run) => _run = run;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    /// <summary>A deep archive query outlives the 60s default; the user is watching a spinner either way.</summary>
    protected override TimeSpan Timeout => TimeSpan.FromMinutes(3);

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "run_search",
        "Run the Search form exactly as its Search button would: build the ADQL from the current fields " +
        "and facets, execute it, show the Results tab, and record it in the user's recent searches. " +
        "Returns the ADQL that ran and the row count. If `truncated` is true the record limit was " +
        "reached and MORE rows match — the sample is incomplete; narrow the query or raise maxRecords.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override Task<SearchRunOutcome> HandleAsync(EmptyArgs args, McpToolContext context, CancellationToken ct) => _run();
}

/// <summary><c>set_adql_query</c> — put ADQL in the editor, and optionally run it.</summary>
public sealed class SetAdqlQueryTool : JsonReadTool<SetAdqlQueryTool.Args, SearchAdqlOutcome>
{
    private readonly Func<string, bool, Task<SearchAdqlOutcome>> _set;

    public SetAdqlQueryTool(Func<string, bool, Task<SearchAdqlOutcome>> set) => _set = set;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    protected override TimeSpan Timeout => TimeSpan.FromMinutes(3);

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "set_adql_query",
        "Put an ADQL query in the Search page's ADQL editor so the user can see and edit it, and " +
        "optionally execute it. Use this rather than search_observations when the point is that the " +
        "user ends up looking at the query and its results.",
        """
        {"type":"object","properties":{
          "adql":{"type":"string","description":"The ADQL query text."},
          "execute":{"type":"boolean","description":"Run it as well as showing it (default false)."}
        },"required":["adql"],"additionalProperties":false}
        """);

    protected override async Task<SearchAdqlOutcome> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var adql = (args.Adql ?? string.Empty).Trim();
        if (adql.Length == 0) throw new McpToolException(new InvalidArgument("adql is required"));
        return await _set(adql, args.Execute ?? false);
    }

    public sealed record Args
    {
        public string? Adql { get; init; }
        public bool? Execute { get; init; }
    }
}

/// <summary><c>run_saved_query</c> — run one of the user's saved ADQL queries by name.</summary>
public sealed class RunSavedQueryTool : JsonReadTool<RunSavedQueryTool.Args, SearchRunOutcome>
{
    private readonly Func<string, Task<SearchRunOutcome>> _run;

    public RunSavedQueryTool(Func<string, Task<SearchRunOutcome>> run) => _run = run;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    protected override TimeSpan Timeout => TimeSpan.FromMinutes(3);

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "run_saved_query",
        "Run one of the user's saved ADQL queries by its exact name (see list_saved_queries) and show " +
        "the results. The query text is loaded into the ADQL editor first, so the user can see what ran.",
        """{"type":"object","properties":{"name":{"type":"string"}},"required":["name"],"additionalProperties":false}""");

    protected override async Task<SearchRunOutcome> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var name = (args.Name ?? string.Empty).Trim();
        if (name.Length == 0) throw new McpToolException(new InvalidArgument("name is required"));
        return await _run(name);
    }

    public sealed record Args { public string? Name { get; init; } }
}

// ── Results ─────────────────────────────────────────────────────────────────────────────────────

/// <summary><c>get_search_results</c> — the rows the grid is showing, with its filters and sort applied.</summary>
public sealed class GetSearchResultsTool : JsonReadTool<GetSearchResultsTool.Args, SearchResultsView>
{
    private readonly Func<SearchResultsQuery, Task<SearchResultsView>> _get;

    public GetSearchResultsTool(Func<SearchResultsQuery, Task<SearchResultsView>> get) => _get = get;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_search_results",
        "Read the live results grid — the rows on the current page, with the user's per-column filters " +
        "and sort applied. `rowColumns` lists EVERY column the query returned (with its display unit and " +
        "any active filter), not just the ones on screen, and is answered even when there are no rows. " +
        "Pass allColumns:true to get every column's cells rather than only the visible ones — the grid " +
        "shows about a dozen of roughly forty.",
        """
        {"type":"object","properties":{
          "allColumns":{"type":"boolean","description":"Return every column's cells, not just the visible ones."},
          "page":{"type":"integer","minimum":1,"description":"Page to read (default: the page on screen)."},
          "limit":{"type":"integer","minimum":1,"maximum":500,"description":"Rows to return (default: the grid's page size)."}
        },"additionalProperties":false}
        """);

    protected override Task<SearchResultsView> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (args.Page is <= 0) throw new McpToolException(new InvalidArgument("page must be 1 or more"));
        if (args.Limit is <= 0) throw new McpToolException(new InvalidArgument("limit must be 1 or more"));
        return _get(new SearchResultsQuery(args.AllColumns ?? false, args.Page, args.Limit));
    }

    public sealed record Args
    {
        public bool? AllColumns { get; init; }
        public int? Page { get; init; }
        public int? Limit { get; init; }
    }
}

/// <summary><c>set_search_results_view</c> — page, sort, filter, hide, re-unit and highlight the grid.</summary>
public sealed class SetSearchResultsViewTool : JsonReadTool<SetSearchResultsViewTool.Args, SearchResultsViewApplied>
{
    private readonly Func<SearchResultsViewPatch, Task<SearchResultsViewApplied>> _set;

    public SetSearchResultsViewTool(Func<SearchResultsViewPatch, Task<SearchResultsViewApplied>> set) => _set = set;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "set_search_results_view",
        "Change how the results are shown: page, page size, sort column and direction, per-column text " +
        "filters, which columns are visible, each column's display unit, and which row is highlighted. " +
        "Column keys match case-insensitively (get_search_results.rowColumns lists them). rowsPerPage " +
        "must be one of the sizes the grid offers, and a display unit a column does not take comes back " +
        "with the units it does.",
        """
        {"type":"object","properties":{
          "page":{"type":"integer","minimum":1},
          "rowsPerPage":{"type":"integer","description":"One of the grid's offered sizes (see rowsPerPageOptions)."},
          "sortColumn":{"type":"string","description":"Column key to sort by."},
          "sortAscending":{"type":"boolean"},
          "filters":{"type":"object","description":"Column key -> filter text (null clears that column's filter).","additionalProperties":{"type":["string","null"]}},
          "visible":{"type":"object","description":"Column key -> whether it is shown.","additionalProperties":{"type":"boolean"}},
          "units":{"type":"object","description":"Column key -> display unit id (null restores the default).","additionalProperties":{"type":["string","null"]}},
          "selectRow":{"type":"integer","minimum":0,"description":"Zero-based row on the current page to highlight and scroll to."},
          "resetFilters":{"type":"boolean","description":"Clear every column filter and the sort."}
        },"additionalProperties":false}
        """);

    protected override async Task<SearchResultsViewApplied> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (args.Page is <= 0) throw new McpToolException(new InvalidArgument("page must be 1 or more"));
        if (args.SelectRow is < 0) throw new McpToolException(new InvalidArgument("selectRow must be 0 or more"));

        var patch = new SearchResultsViewPatch(
            args.Page, args.RowsPerPage, string.IsNullOrWhiteSpace(args.SortColumn) ? null : args.SortColumn!.Trim(),
            args.SortAscending, args.Filters, args.Visible, args.Units, args.SelectRow, args.ResetFilters ?? false);

        var applied = await _set(patch);

        if (applied.UnknownColumns.Count > 0)
            throw new McpToolException(new InvalidArgument(
                $"no such column: {string.Join(", ", applied.UnknownColumns)}. " +
                $"Columns: {string.Join(", ", applied.View.RowColumns.Select(c => c.Key))}"));

        if (applied.RejectedUnits.Count > 0)
        {
            var detail = string.Join("; ", applied.RejectedUnits.Select(r =>
                $"'{r.Column}' does not take the unit '{r.Requested}'" +
                (r.Accepts.Count > 0 ? $" (it takes: {string.Join(", ", r.Accepts)})" : " (it has no unit menu)")));
            throw new McpToolException(new InvalidArgument(detail));
        }

        return applied;
    }

    public sealed record Args
    {
        public int? Page { get; init; }
        public int? RowsPerPage { get; init; }
        public string? SortColumn { get; init; }
        public bool? SortAscending { get; init; }
        public Dictionary<string, string?>? Filters { get; init; }
        public Dictionary<string, bool>? Visible { get; init; }
        public Dictionary<string, string?>? Units { get; init; }
        public int? SelectRow { get; init; }
        public bool? ResetFilters { get; init; }
    }
}

/// <summary><c>export_search_results</c> — write the current results to a CSV or TSV file.</summary>
public sealed class ExportSearchResultsTool : JsonReadTool<ExportSearchResultsTool.Args, SearchExportOutcome>
{
    private readonly Func<string, string, Task<SearchExportOutcome>> _export;

    public ExportSearchResultsTool(Func<string, string, Task<SearchExportOutcome>> export) => _export = export;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "export_search_results",
        "Write the current search results to a local CSV or TSV file — the same export the Results tab's " +
        "buttons produce, with every column the query returned (not only the visible ones).",
        """
        {"type":"object","properties":{
          "format":{"type":"string","enum":["csv","tsv"]},
          "path":{"type":"string","description":"Absolute local path to write."}
        },"required":["format","path"],"additionalProperties":false}
        """);

    protected override async Task<SearchExportOutcome> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var format = (args.Format ?? string.Empty).Trim().ToLowerInvariant();
        if (format is not ("csv" or "tsv"))
            throw new McpToolException(new InvalidArgument("format must be 'csv' or 'tsv'"));

        var path = (args.Path ?? string.Empty).Trim();
        if (path.Length == 0) throw new McpToolException(new InvalidArgument("path is required"));
        if (!Path.IsPathRooted(path)) throw new McpToolException(new InvalidArgument("path must be absolute"));

        return await _export(format, path);
    }

    public sealed record Args
    {
        public string? Format { get; init; }
        public string? Path { get; init; }
    }
}

// ── Detail + history ────────────────────────────────────────────────────────────────────────────

/// <summary><c>show_search_row_detail</c> — open the CAOM2 detail page for a results row.</summary>
public sealed class ShowSearchRowDetailTool : JsonReadTool<ShowSearchRowDetailTool.Args, SearchRowDetailOutcome>
{
    private readonly Func<int?, Task<SearchRowDetailOutcome>> _show;

    public ShowSearchRowDetailTool(Func<int?, Task<SearchRowDetailOutcome>> show) => _show = show;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "show_search_row_detail",
        "Open the full CAOM2 observation detail for a row of the results grid — the same view a click on " +
        "the row gives. Omit `row` to open the highlighted row (set one with " +
        "set_search_results_view.selectRow). `row` is zero-based within the page on screen.",
        """{"type":"object","properties":{"row":{"type":"integer","minimum":0}},"additionalProperties":false}""");

    protected override async Task<SearchRowDetailOutcome> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (args.Row is < 0) throw new McpToolException(new InvalidArgument("row must be 0 or more"));
        return await _show(args.Row);
    }

    public sealed record Args { public int? Row { get; init; } }
}

/// <summary><c>show_observation_detail</c> — open the CAOM2 detail page for a publisher id.</summary>
public sealed class ShowObservationDetailTool : JsonReadTool<ShowObservationDetailTool.Args, SearchRowDetailOutcome>
{
    private readonly Func<string, Task<SearchRowDetailOutcome>> _show;

    public ShowObservationDetailTool(Func<string, Task<SearchRowDetailOutcome>> show) => _show = show;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "show_observation_detail",
        "Open one observation's full CAOM2 detail page (overview, coverage, files, provenance, raw " +
        "metadata) by its publisher id — the `publisher_id` column of a search result, or the id from " +
        "get_observation_caom2. Unlike get_observation_caom2 this puts it on the user's screen.",
        """{"type":"object","properties":{"publisherId":{"type":"string"}},"required":["publisherId"],"additionalProperties":false}""");

    protected override async Task<SearchRowDetailOutcome> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var id = (args.PublisherId ?? string.Empty).Trim();
        if (id.Length == 0) throw new McpToolException(new InvalidArgument("publisherId is required"));
        return await _show(id);
    }

    public sealed record Args { public string? PublisherId { get; init; } }
}

/// <summary><c>remove_recent_search</c> — drop one entry from the recent-searches rail.</summary>
public sealed class RemoveRecentSearchTool : JsonReadTool<RemoveRecentSearchTool.Args, SearchRecentRemoved>
{
    private readonly Func<string, Task<SearchRecentRemoved>> _remove;

    public RemoveRecentSearchTool(Func<string, Task<SearchRecentRemoved>> remove) => _remove = remove;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "remove_recent_search",
        "Remove one entry from the Search page's recent-searches rail, matched by its summary or by its " +
        "exact ADQL (see list_recent_searches). One entry — clear_recent_searches empties the whole rail.",
        """{"type":"object","properties":{"match":{"type":"string","description":"The entry's summary, or its exact ADQL."}},"required":["match"],"additionalProperties":false}""");

    protected override async Task<SearchRecentRemoved> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var match = (args.Match ?? string.Empty).Trim();
        if (match.Length == 0) throw new McpToolException(new InvalidArgument("match is required"));
        return await _remove(match);
    }

    public sealed record Args { public string? Match { get; init; } }
}

/// <summary>
/// <c>reset_search_form</c> — empty the form, the way the Clear button does.
///
/// An agent that had filled in six fields for one target and wanted a different one had to overwrite
/// each of them by name, and any it forgot silently narrowed the next search. Worse, the Additional
/// Constraints facets are not form fields: a tick left in one of those columns constrains a query with
/// nothing on screen to say so.
/// </summary>
public sealed class ResetSearchFormTool : JsonReadTool<EmptyArgs, SearchFormApplied>
{
    private readonly Func<Task<SearchFormApplied>> _reset;

    public ResetSearchFormTool(Func<Task<SearchFormApplied>> reset) => _reset = reset;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "reset_search_form",
        "Empty the Search form — every field AND every Additional Constraints facet, which is what the " +
        "Clear button does. Call this between unrelated searches: a value left in a field you did not " +
        "overwrite, or a facet left ticked, silently narrows the next query. Returns the empty form. " +
        "The recent-searches rail and the saved queries are untouched.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override Task<SearchFormApplied> HandleAsync(EmptyArgs args, McpToolContext context, CancellationToken ct)
        => _reset();
}

/// <summary>
/// <c>load_recent_search</c> — put one of the recent searches back in the form.
///
/// The rail could be read and its entries deleted, but not USED, which is the one thing the rail is
/// for. Matching is the same as remove_recent_search's, so a string read out of list_recent_searches
/// works in either.
/// </summary>
public sealed class LoadRecentSearchTool : JsonReadTool<LoadRecentSearchTool.Args, SearchFormApplied>
{
    private readonly Func<string, Task<SearchFormApplied>> _load;

    public LoadRecentSearchTool(Func<string, Task<SearchFormApplied>> load) => _load = load;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "load_recent_search",
        "Load one of the Search page's recent searches back into the form, matched by its summary or by " +
        "its exact ADQL (see list_recent_searches). Fills the fields and the Additional Constraints " +
        "facets and returns the form; it does NOT run the search — call run_search when you want that.",
        """{"type":"object","properties":{"match":{"type":"string","description":"The entry's summary, or its exact ADQL."}},"required":["match"],"additionalProperties":false}""");

    protected override async Task<SearchFormApplied> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var match = (args.Match ?? string.Empty).Trim();
        if (match.Length == 0) throw new McpToolException(new InvalidArgument("match is required"));
        return await _load(match);
    }

    public sealed record Args { public string? Match { get; init; } }
}

/// <summary><c>clear_recent_searches</c> — empty the rail, rather than a call per entry.</summary>
public sealed class ClearRecentSearchesTool : JsonReadTool<EmptyArgs, SearchRecentRemoved>
{
    private readonly Func<Task<SearchRecentRemoved>> _clear;

    public ClearRecentSearchesTool(Func<Task<SearchRecentRemoved>> clear) => _clear = clear;

    /// <summary>Destructive: the rail is the only record of those searches, and nothing brings it back.</summary>
    public override McpVerbClass VerbClass => McpVerbClass.Destructive;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "clear_recent_searches",
        "Empty the Search page's recent-searches rail. There is no undo, and the rail is the only record " +
        "of those queries — save one with save_query first if it is worth keeping. Saved queries are a " +
        "separate list and are NOT affected. Use remove_recent_search when you mean one entry.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override Task<SearchRecentRemoved> HandleAsync(EmptyArgs args, McpToolContext context, CancellationToken ct)
        => _clear();
}
