using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Read;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

/// <summary>
/// The live Search-page steering tools (Read/SearchUiReadTools.cs + Write/SearchUiTools.cs): argument
/// validation, delegate pass-through, verb classes, and the "Search page unavailable" fallback.
/// </summary>
public class SearchUiToolsTests
{
    private static readonly McpToolContext Ctx = McpToolContext.ForExternal("c1", Guid.Empty);
    private static JsonValue Args(string json) => JsonValue.Parse(json);
    private static JsonElement Json(ToolResult result) => JsonDocument.Parse(Assert.IsType<DataResult>(result).Json).RootElement;

    private static readonly SearchFormSnapshot EmptyForm = new();
    private static readonly SearchFacetsSnapshot EmptyFacets = new() { Loaded = true, RowCount = 1 };

    // ── get_search_form ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetSearchForm_ReturnsSnapshot()
    {
        var tool = new GetSearchFormTool(() => Task.FromResult<SearchFormSnapshot?>(
            new SearchFormSnapshot { Target = "M31", ResolvedRa = 10.68, MaxRecords = 5000 }));

        var doc = Json(await tool.InvokeAsync(Args("{}"), Ctx, default));

        Assert.Equal("M31", doc.GetProperty("target").GetString());
        Assert.Equal(10.68, doc.GetProperty("resolvedRa").GetDouble());
        Assert.Equal(5000, doc.GetProperty("maxRecords").GetInt32());
    }

    [Fact]
    public async Task GetSearchForm_UiUnavailable_BackendError()
    {
        var tool = new GetSearchFormTool(() => Task.FromResult<SearchFormSnapshot?>(null));
        var result = await tool.InvokeAsync(Args("{}"), Ctx, default);
        Assert.IsType<BackendError>(Assert.IsType<FailedResult>(result).Reason);
    }

    // ── set_search_form ───────────────────────────────────────────────────────

    [Fact]
    public async Task SetSearchForm_PassesPatch_TimeSpanAliasBinds()
    {
        SearchFormPatch? seen = null;
        var tool = new SetSearchFormTool(p => { seen = p; return Task.FromResult<SearchFormSnapshot?>(EmptyForm); });

        await tool.InvokeAsync(Args("""{"target":"NGC 1275","radiusDeg":0.5,"publicOnly":true,"timeSpan":"1..10"}"""), Ctx, default);

        Assert.NotNull(seen);
        Assert.Equal("NGC 1275", seen!.Target);
        Assert.Equal(0.5, seen.RadiusDeg);
        Assert.True(seen.PublicOnly);
        Assert.Equal("1..10", seen.TimeSpanRange); // JSON name "timeSpan"
        Assert.Null(seen.ObservationId);           // untouched fields stay null (patch semantics)
    }

    [Theory]
    [InlineData("""{"intent":"junk"}""")]
    [InlineData("""{"resolver":"GAIA"}""")]
    [InlineData("""{"datePreset":"LastYear"}""")]
    [InlineData("""{"pixelScaleUnit":"parsec"}""")]
    [InlineData("""{"integrationTimeUnit":"weeks"}""")]
    [InlineData("""{"spectralCoverageUnit":"furlong"}""")]
    [InlineData("""{"radiusDeg":-1}""")]
    [InlineData("""{"maxRecords":0}""")]
    [InlineData("""{"maxRecords":50000}""")]
    public async Task SetSearchForm_InvalidValues_InvalidArgument(string json)
    {
        var tool = new SetSearchFormTool(_ => Task.FromResult<SearchFormSnapshot?>(EmptyForm));
        var result = await tool.InvokeAsync(Args(json), Ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
    }

    [Fact]
    public void SearchUiWriteTools_AreViewStateVerb()
    {
        Assert.Equal(McpVerbClass.ViewState, new SetSearchFormTool(_ => Task.FromResult<SearchFormSnapshot?>(null)).VerbClass);
        Assert.Equal(McpVerbClass.ViewState, new SetSearchConstraintsTool(_ => Task.FromResult<SearchConstraintsOutcome?>(null)).VerbClass);
        Assert.Equal(McpVerbClass.ViewState, new ResetSearchFormTool(() => Task.FromResult<SearchFormSnapshot?>(null)).VerbClass);
        Assert.Equal(McpVerbClass.ViewState, new RunSearchTool(() => Task.FromResult(new SearchRunOutcome(true, "q", 1, "ok", null))).VerbClass);
        Assert.Equal(McpVerbClass.ViewState, new SetAdqlQueryTool(_ => Task.FromResult<AdqlStageOutcome?>(null)).VerbClass);
        Assert.Equal(McpVerbClass.ViewState, new ExecuteAdqlQueryTool(_ => Task.FromResult(new SearchRunOutcome(true, "q", 1, "ok", null))).VerbClass);
        Assert.Equal(McpVerbClass.ViewState, new SetSearchResultsViewTool(_ => Task.FromResult<SearchResultsSnapshot?>(null)).VerbClass);
        Assert.Equal(McpVerbClass.ViewState, new ExportSearchResultsTool((_, _) => Task.FromResult(new SearchExportOutcome(true, "p", 1, null))).VerbClass);
        Assert.Equal(McpVerbClass.ViewState, new LoadRecentSearchTool(_ => Task.FromResult<LoadRecentSearchOutcome?>(null)).VerbClass);
        Assert.Equal(McpVerbClass.ViewState, new RunSavedQueryTool(_ => Task.FromResult(new SearchRunOutcome(true, "q", 1, "ok", null))).VerbClass);
    }

    // ── set_search_constraints ────────────────────────────────────────────────

    [Fact]
    public async Task SetSearchConstraints_NoFacets_InvalidArgument()
    {
        var tool = new SetSearchConstraintsTool(_ => Task.FromResult<SearchConstraintsOutcome?>(null));
        var result = await tool.InvokeAsync(Args("{}"), Ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
    }

    [Fact]
    public async Task SetSearchConstraints_PassesSelections_ReportsDropped()
    {
        SearchFacetSelections? seen = null;
        var tool = new SetSearchConstraintsTool(s =>
        {
            seen = s;
            return Task.FromResult<SearchConstraintsOutcome?>(new SearchConstraintsOutcome(true, ["BOGUS"], EmptyFacets));
        });

        var doc = Json(await tool.InvokeAsync(Args("""{"collections":["CFHT","BOGUS"],"bands":["Optical"]}"""), Ctx, default));

        Assert.Equal(new[] { "CFHT", "BOGUS" }, seen!.Collections!);
        Assert.Equal(new[] { "Optical" }, seen.Bands!);
        Assert.Null(seen.Instruments);
        Assert.True(doc.GetProperty("applied").GetBoolean());
        Assert.Equal("BOGUS", doc.GetProperty("dropped")[0].GetString());
    }

    [Fact]
    public async Task SetSearchConstraints_ClearAllAlone_IsValid()
    {
        SearchFacetSelections? seen = null;
        var tool = new SetSearchConstraintsTool(s =>
        {
            seen = s;
            return Task.FromResult<SearchConstraintsOutcome?>(new SearchConstraintsOutcome(true, [], EmptyFacets));
        });

        var result = await tool.InvokeAsync(Args("""{"clearAll":true}"""), Ctx, default);

        Assert.IsType<DataResult>(result);
        Assert.True(seen!.ClearAll);
    }

    // ── get_search_constraints ────────────────────────────────────────────────

    [Fact]
    public async Task GetSearchConstraints_ReturnsFacets()
    {
        var facets = new SearchFacetsSnapshot
        {
            Loaded = true,
            RowCount = 42,
            Collections = new SearchFacetView(["CFHT", "JWST"], ["CFHT"]),
        };
        var tool = new GetSearchConstraintsTool(() => Task.FromResult<SearchFacetsSnapshot?>(facets));

        var doc = Json(await tool.InvokeAsync(Args("{}"), Ctx, default));

        Assert.True(doc.GetProperty("loaded").GetBoolean());
        Assert.Equal(42, doc.GetProperty("rowCount").GetInt32());
        Assert.Equal("JWST", doc.GetProperty("collections").GetProperty("available")[1].GetString());
        Assert.Equal("CFHT", doc.GetProperty("collections").GetProperty("selected")[0].GetString());
    }

    // ── run_search / ADQL ─────────────────────────────────────────────────────

    [Fact]
    public async Task RunSearch_ReturnsOutcome()
    {
        var tool = new RunSearchTool(() => Task.FromResult(new SearchRunOutcome(true, "SELECT 1", 7, "7 rows returned", null)));
        var doc = Json(await tool.InvokeAsync(Args("{}"), Ctx, default));
        Assert.True(doc.GetProperty("ran").GetBoolean());
        Assert.Equal(7, doc.GetProperty("totalRows").GetInt32());
        Assert.Equal("SELECT 1", doc.GetProperty("adql").GetString());
    }

    [Fact]
    public async Task SetAdqlQuery_EmptyAdql_InvalidArgument()
    {
        var tool = new SetAdqlQueryTool(_ => Task.FromResult<AdqlStageOutcome?>(null));
        var result = await tool.InvokeAsync(Args("""{"adql":"  "}"""), Ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
    }

    [Fact]
    public async Task SetAdqlQuery_StagesTrimmedText()
    {
        string? staged = null;
        var tool = new SetAdqlQueryTool(a => { staged = a; return Task.FromResult<AdqlStageOutcome?>(new AdqlStageOutcome(true, a)); });
        var doc = Json(await tool.InvokeAsync(Args("""{"adql":"  SELECT TOP 5 * FROM caom2.Plane  "}"""), Ctx, default));
        Assert.Equal("SELECT TOP 5 * FROM caom2.Plane", staged);
        Assert.True(doc.GetProperty("applied").GetBoolean());
    }

    [Fact]
    public async Task ExecuteAdqlQuery_OmittedAdql_PassesNull()
    {
        var seen = "sentinel";
        var tool = new ExecuteAdqlQueryTool(a => { seen = a; return Task.FromResult(new SearchRunOutcome(true, "q", 1, "ok", null)); });
        await tool.InvokeAsync(Args("{}"), Ctx, default);
        Assert.Null(seen);
    }

    // ── get_search_results / set_search_results_view ──────────────────────────

    [Fact]
    public async Task GetSearchResults_DefaultsIncludeRows_CapsMaxRows()
    {
        (bool includeRows, int maxRows) seen = default;
        var tool = new GetSearchResultsTool((inc, max) =>
        {
            seen = (inc, max);
            return Task.FromResult<SearchResultsSnapshot?>(new SearchResultsSnapshot { HasResults = false });
        });

        await tool.InvokeAsync(Args("{}"), Ctx, default);
        Assert.True(seen.includeRows);
        Assert.Equal(GetSearchResultsTool.MaxRowsCap, seen.maxRows);

        await tool.InvokeAsync(Args("""{"includeRows":false,"maxRows":10000}"""), Ctx, default);
        Assert.False(seen.includeRows);
        Assert.Equal(GetSearchResultsTool.MaxRowsCap, seen.maxRows); // capped
    }

    [Fact]
    public async Task SetSearchResultsView_NoDirectives_InvalidArgument()
    {
        var tool = new SetSearchResultsViewTool(_ => Task.FromResult<SearchResultsSnapshot?>(null));
        var result = await tool.InvokeAsync(Args("{}"), Ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
    }

    [Theory]
    [InlineData("""{"pageAction":"sideways"}""")]
    [InlineData("""{"page":0}""")]
    [InlineData("""{"rowsPerPage":33}""")]
    public async Task SetSearchResultsView_InvalidValues_InvalidArgument(string json)
    {
        var tool = new SetSearchResultsViewTool(_ => Task.FromResult<SearchResultsSnapshot?>(null));
        var result = await tool.InvokeAsync(Args(json), Ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
    }

    [Fact]
    public async Task SetSearchResultsView_PassesCommand()
    {
        SearchResultsCommand? seen = null;
        var tool = new SetSearchResultsViewTool(c =>
        {
            seen = c;
            return Task.FromResult<SearchResultsSnapshot?>(new SearchResultsSnapshot { HasResults = true, CurrentPage = 2 });
        });

        var doc = Json(await tool.InvokeAsync(
            Args("""{"page":2,"sortColumn":"collection","sortAscending":false,"setFilters":{"instrument":"MegaPrime"}}"""), Ctx, default));

        Assert.Equal(2, seen!.Page);
        Assert.Equal("collection", seen.SortColumn);
        Assert.False(seen.SortAscending!.Value);
        Assert.Equal("MegaPrime", seen.SetFilters!["instrument"]);
        Assert.Equal(2, doc.GetProperty("currentPage").GetInt32());
    }

    // ── export_search_results ─────────────────────────────────────────────────

    [Fact]
    public async Task ExportSearchResults_BadFormat_InvalidArgument()
    {
        var tool = new ExportSearchResultsTool((_, _) => Task.FromResult(new SearchExportOutcome(true, "p", 1, null)));
        var result = await tool.InvokeAsync(Args("""{"format":"xlsx"}"""), Ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
    }

    [Fact]
    public async Task ExportSearchResults_NormalizesFormat_PassesPath()
    {
        (string format, string? path) seen = default;
        var tool = new ExportSearchResultsTool((f, p) =>
        {
            seen = (f, p);
            return Task.FromResult(new SearchExportOutcome(true, p ?? "default", 3, null));
        });

        var doc = Json(await tool.InvokeAsync(Args("""{"format":"CSV","path":"C:/tmp/out.csv"}"""), Ctx, default));

        Assert.Equal("csv", seen.format);
        Assert.Equal("C:/tmp/out.csv", seen.path);
        Assert.True(doc.GetProperty("exported").GetBoolean());
    }

    // ── load_recent_search / run_saved_query ──────────────────────────────────

    [Fact]
    public async Task LoadRecentSearch_MissingIndex_InvalidArgument()
    {
        var tool = new LoadRecentSearchTool(_ => Task.FromResult<LoadRecentSearchOutcome?>(null));
        var result = await tool.InvokeAsync(Args("{}"), Ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
    }

    [Fact]
    public async Task LoadRecentSearch_PassesIndex_ReportsOutcome()
    {
        var tool = new LoadRecentSearchTool(i => Task.FromResult<LoadRecentSearchOutcome?>(
            new LoadRecentSearchOutcome(true, $"search {i}", null, EmptyForm)));
        var doc = Json(await tool.InvokeAsync(Args("""{"index":3}"""), Ctx, default));
        Assert.True(doc.GetProperty("loaded").GetBoolean());
        Assert.Equal("search 3", doc.GetProperty("summary").GetString());
    }

    [Fact]
    public async Task RunSavedQuery_EmptyName_InvalidArgument()
    {
        var tool = new RunSavedQueryTool(_ => Task.FromResult(new SearchRunOutcome(true, "q", 1, "ok", null)));
        var result = await tool.InvokeAsync(Args("""{"name":" "}"""), Ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
    }

    [Fact]
    public async Task RunSavedQuery_PassesTrimmedName()
    {
        string? seen = null;
        var tool = new RunSavedQueryTool(n => { seen = n; return Task.FromResult(new SearchRunOutcome(true, "q", 1, "ok", null)); });
        await tool.InvokeAsync(Args("""{"name":" My cone "}"""), Ctx, default);
        Assert.Equal("My cone", seen);
    }

    // ── read tools stay Read / agent-safe ─────────────────────────────────────

    [Fact]
    public void ReadTools_AreReadVerb()
    {
        Assert.Equal(McpVerbClass.Read, new GetSearchFormTool(() => Task.FromResult<SearchFormSnapshot?>(null)).VerbClass);
        Assert.Equal(McpVerbClass.Read, new GetSearchConstraintsTool(() => Task.FromResult<SearchFacetsSnapshot?>(null)).VerbClass);
        Assert.Equal(McpVerbClass.Read, new GetSearchResultsTool((_, _) => Task.FromResult<SearchResultsSnapshot?>(null)).VerbClass);
    }
}
