using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

/// <summary>
/// The search_* UI tools. Each takes one delegate, so the "page" here is a lambda — these tests are
/// about the tools' own job: converting arguments, refusing what cannot work, and turning a bridge's
/// rejection into a message that names both the mistake and the vocabulary that would have worked.
/// </summary>
public class SearchUiToolsTests
{
    private static JsonValue Args(string json) => JsonValue.Parse(json);
    private static McpToolContext Ctx() => McpToolContext.ForExternal("c1", Guid.NewGuid());

    private static T Payload<T>(ToolResult result)
    {
        var data = Assert.IsType<DataResult>(result);
        return JsonSerializer.Deserialize<T>(data.Json, McpJson.Options)!;
    }

    private static string Failure(ToolResult result) => Assert.IsType<FailedResult>(result).Reason.Description;

    private static SearchFormView EmptyForm() => new(
        true, Array.Empty<FormFieldView>(), null, null, "ALL", "", 10000, "", "form");

    private static SearchResultsView EmptyResults(params string[] columns) => new(
        true, 0, 0, 1, 1, 50, [25, 50, 100],
        null, true, Array.Empty<string>(),
        columns.Select(c => new ResultColumnView(c, c, c, true)).ToList(),
        null, Array.Empty<IReadOnlyList<string>>());

    // ── The vocabulary the schemas advertise ────────────────────────────────────────────────────

    [Fact]
    public void SetSearchForm_SchemaAdvertisesEveryKnownField()
    {
        var schema = new SetSearchFormTool(_ => Task.FromResult(
            new SearchFormApplied(true, [], [], [], EmptyForm()))).Descriptor.InputSchema.ToJsonString();

        Assert.All(SearchToolArgs.FormFields, field => Assert.Contains($"\"{field}\"", schema));
        // additionalProperties:false is what makes the list a vocabulary rather than a suggestion.
        Assert.Contains("\"additionalProperties\":false", schema.Replace(" ", ""));
    }

    [Fact]
    public void SetSearchConstraints_SchemaAdvertisesEveryFacet()
    {
        var schema = new SetSearchConstraintsTool(_ => Task.FromResult(
            new SearchConstraintsApplied(true, [], [], [], [], new SearchConstraintsView(true, [])))).Descriptor.InputSchema.ToJsonString();

        Assert.All(SearchToolArgs.Facets, facet => Assert.Contains($"\"{facet}\"", schema));
    }

    [Fact]
    public void EverySearchToolIsViewStateAndNamedOnce()
    {
        IMcpTool[] tools =
        [
            new GetSearchFormTool(() => Task.FromResult(EmptyForm())),
            new SetSearchFormTool(_ => Task.FromResult(new SearchFormApplied(true, [], [], [], EmptyForm()))),
            new GetSearchConstraintsTool(() => Task.FromResult(new SearchConstraintsView(true, []))),
            new SetSearchConstraintsTool(_ => Task.FromResult(new SearchConstraintsApplied(true, [], [], [], [], new SearchConstraintsView(true, [])))),
            new RunSearchTool(() => Task.FromResult(new SearchRunOutcome(true, "SELECT 1", 0, false, 10, 1))),
            new SetAdqlQueryTool((a, _) => Task.FromResult(new SearchAdqlOutcome(true, a, false))),
            new RunSavedQueryTool(_ => Task.FromResult(new SearchRunOutcome(true, "SELECT 1", 0, false, 10, 1))),
            new GetSearchResultsTool(_ => Task.FromResult(EmptyResults())),
            new SetSearchResultsViewTool(_ => Task.FromResult(new SearchResultsViewApplied(true, [], [], [], EmptyResults()))),
            new ExportSearchResultsTool((f, p) => Task.FromResult(new SearchExportOutcome(true, f, p, 0, 0))),
            new ShowSearchRowDetailTool(_ => Task.FromResult(new SearchRowDetailOutcome(true, 0, "p"))),
            new ShowObservationDetailTool(_ => Task.FromResult(new SearchRowDetailOutcome(true, null, "p"))),
            new RemoveRecentSearchTool(_ => Task.FromResult(new SearchRecentRemoved(true, "s", 0))),
        ];

        Assert.All(tools, t => Assert.Equal(McpVerbClass.ViewState, t.VerbClass));
        Assert.All(tools, t => Assert.True(t.AgentSafe));

        var names = tools.Select(t => t.Descriptor.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.Equal(
        [
            "export_search_results", "get_search_constraints", "get_search_form", "get_search_results",
            "remove_recent_search", "run_saved_query", "run_search", "set_adql_query",
            "set_search_constraints", "set_search_form", "set_search_results_view",
            "show_observation_detail", "show_search_row_detail",
        ], names);
    }

    // ── set_search_form ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetSearchForm_PassesFieldsThroughAsText()
    {
        SearchFormPatch? seen = null;
        var tool = new SetSearchFormTool(p => { seen = p; return Task.FromResult(new SearchFormApplied(true, ["target"], [], [], EmptyForm())); });

        var result = await tool.InvokeAsync(
            Args("""{"target":"M31","searchRadius":0.5,"publicOnly":true,"proposalId":null}"""), Ctx(), default);

        var applied = Payload<SearchFormApplied>(result);
        Assert.True(applied.Applied);

        Assert.NotNull(seen);
        Assert.Equal("M31", seen!.Fields["target"]);
        // A number arrives as a number and is written in its invariant form — an agent that sends 0.5
        // rather than "0.5" means the same thing.
        Assert.Equal("0.5", seen.Fields["searchRadius"]);
        Assert.Equal("true", seen.Fields["publicOnly"]);
        Assert.Null(seen.Fields["proposalId"]);
    }

    [Fact]
    public async Task SetSearchForm_UnknownFieldIsRefusedByNameWithTheVocabulary()
    {
        var tool = new SetSearchFormTool(_ => Task.FromResult(
            new SearchFormApplied(false, [], ["telescope"], ["target", "observationId"], EmptyForm(), "nothing applied")));

        var message = Failure(await tool.InvokeAsync(Args("""{"telescope":"CFHT"}"""), Ctx(), default));

        Assert.Contains("telescope", message);
        Assert.Contains("Known fields", message);
        Assert.Contains("target", message);
    }

    [Fact]
    public async Task SetSearchForm_RejectsAStructuredValue()
    {
        var tool = new SetSearchFormTool(_ => Task.FromResult(new SearchFormApplied(true, [], [], [], EmptyForm())));
        var message = Failure(await tool.InvokeAsync(Args("""{"target":{"name":"M31"}}"""), Ctx(), default));

        Assert.Contains("target", message);
        Assert.Contains("object", message);
    }

    [Fact]
    public async Task SetSearchForm_EmptyPatchAsksForAField()
    {
        var tool = new SetSearchFormTool(_ => Task.FromResult(new SearchFormApplied(true, [], [], [], EmptyForm())));
        var message = Failure(await tool.InvokeAsync(Args("{}"), Ctx(), default));

        Assert.Contains("at least one field", message);
        Assert.Contains("target", message);
    }

    // ── set_search_constraints ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetSearchConstraints_AcceptsAnArrayOrOneString()
    {
        SearchConstraintsPatch? seen = null;
        var tool = new SetSearchConstraintsTool(p =>
        {
            seen = p;
            return Task.FromResult(new SearchConstraintsApplied(true, ["collections"], [], [], [], new SearchConstraintsView(true, [])));
        });

        await tool.InvokeAsync(Args("""{"collections":["CFHT","JWST"],"bands":"Optical"}"""), Ctx(), default);

        Assert.Equal(["CFHT", "JWST"], seen!.Facets["collections"]);
        Assert.Equal(["Optical"], seen.Facets["bands"]);
    }

    [Fact]
    public async Task SetSearchConstraints_ValueTheFacetDoesNotOfferNamesWhatIsAvailable()
    {
        var tool = new SetSearchConstraintsTool(_ => Task.FromResult(new SearchConstraintsApplied(
            false, [], [],
            [new FacetValueRejection("collections", "CHFT", ["CFHT", "CFHTMEGAPIPE"])],
            SearchToolArgs.Facets, new SearchConstraintsView(true, []), "nothing was applied")));

        var message = Failure(await tool.InvokeAsync(Args("""{"collections":["CHFT"]}"""), Ctx(), default));

        Assert.Contains("does not offer 'CHFT'", message);
        Assert.Contains("CFHT", message);
    }

    [Fact]
    public async Task SetSearchConstraints_UnknownFacetIsRefusedByName()
    {
        var tool = new SetSearchConstraintsTool(_ => Task.FromResult(new SearchConstraintsApplied(
            false, [], ["telescopes"], [], SearchToolArgs.Facets, new SearchConstraintsView(true, []))));

        var message = Failure(await tool.InvokeAsync(Args("""{"telescopes":["CFHT"]}"""), Ctx(), default));

        Assert.Contains("telescopes", message);
        Assert.Contains("collections", message);
    }

    // ── Results ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSearchResults_DefaultsToTheVisibleColumnsAndPassesAllColumnsThrough()
    {
        SearchResultsQuery? seen = null;
        var tool = new GetSearchResultsTool(q => { seen = q; return Task.FromResult(EmptyResults("obsid")); });

        await tool.InvokeAsync(Args("{}"), Ctx(), default);
        Assert.False(seen!.AllColumns);

        await tool.InvokeAsync(Args("""{"allColumns":true,"page":2,"limit":10}"""), Ctx(), default);
        Assert.True(seen!.AllColumns);
        Assert.Equal(2, seen.Page);
        Assert.Equal(10, seen.Limit);
    }

    [Fact]
    public async Task GetSearchResults_AnswersTheColumnVocabularyWithNoRows()
    {
        var tool = new GetSearchResultsTool(_ => Task.FromResult(EmptyResults("obsid", "target")));
        var view = Payload<SearchResultsView>(await tool.InvokeAsync(Args("{}"), Ctx(), default));

        Assert.Empty(view.Rows);
        Assert.Equal(["obsid", "target"], view.RowColumns.Select(c => c.Key));
    }

    [Fact]
    public async Task GetSearchResults_RefusesAZeroPage()
    {
        var tool = new GetSearchResultsTool(_ => Task.FromResult(EmptyResults()));
        Assert.Contains("page must be 1 or more", Failure(await tool.InvokeAsync(Args("""{"page":0}"""), Ctx(), default)));
    }

    [Fact]
    public async Task SetSearchResultsView_UnknownColumnNamesTheColumnsThatExist()
    {
        var tool = new SetSearchResultsViewTool(_ => Task.FromResult(new SearchResultsViewApplied(
            false, [], ["exposure"], [], EmptyResults("obsid", "inttime"), "nothing was applied")));

        var message = Failure(await tool.InvokeAsync(Args("""{"sortColumn":"exposure"}"""), Ctx(), default));

        Assert.Contains("exposure", message);
        Assert.Contains("inttime", message);
    }

    [Fact]
    public async Task SetSearchResultsView_RejectedUnitNamesTheUnitsTheColumnTakes()
    {
        var tool = new SetSearchResultsViewTool(_ => Task.FromResult(new SearchResultsViewApplied(
            false, [], [],
            [new UnitRejection("inttime", "parsecs", ["seconds", "minutes", "hours", "days"])],
            EmptyResults("inttime"))));

        var message = Failure(await tool.InvokeAsync(Args("""{"units":{"inttime":"parsecs"}}"""), Ctx(), default));

        Assert.Contains("does not take the unit 'parsecs'", message);
        Assert.Contains("seconds", message);
    }

    [Fact]
    public async Task SetSearchResultsView_PassesEveryPartOfThePatchThrough()
    {
        SearchResultsViewPatch? seen = null;
        var tool = new SetSearchResultsViewTool(p =>
        {
            seen = p;
            return Task.FromResult(new SearchResultsViewApplied(true, ["page"], [], [], EmptyResults("obsid")));
        });

        await tool.InvokeAsync(Args("""
            {"page":3,"rowsPerPage":100,"sortColumn":"obsid","sortAscending":false,
             "filters":{"obsid":"CF"},"visible":{"obsid":true},"units":{"obsid":null},
             "selectRow":2,"resetFilters":true}
            """), Ctx(), default);

        Assert.Equal(3, seen!.Page);
        Assert.Equal(100, seen.RowsPerPage);
        Assert.Equal("obsid", seen.SortColumn);
        Assert.False(seen.SortAscending);
        Assert.Equal("CF", seen.Filters!["obsid"]);
        Assert.True(seen.Visible!["obsid"]);
        Assert.Null(seen.Units!["obsid"]);
        Assert.Equal(2, seen.SelectRow);
        Assert.True(seen.ResetFilters);
    }

    // ── Export ──────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("""{"format":"xlsx","path":"C:\\tmp\\out.xlsx"}""", "format must be")]
    [InlineData("""{"format":"csv","path":"out.csv"}""", "must be absolute")]
    public async Task ExportSearchResults_RefusesWhatItCannotWrite(string args, string expected)
    {
        var tool = new ExportSearchResultsTool((f, p) => Task.FromResult(new SearchExportOutcome(true, f, p, 1, 1)));
        Assert.Contains(expected, Failure(await tool.InvokeAsync(Args(args), Ctx(), default)));
    }

    // ── Detail + history ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ShowSearchRowDetail_WithoutARowMeansTheHighlightedOne()
    {
        int? seen = -1;
        var tool = new ShowSearchRowDetailTool(r => { seen = r; return Task.FromResult(new SearchRowDetailOutcome(true, r, "pid")); });

        await tool.InvokeAsync(Args("{}"), Ctx(), default);
        Assert.Null(seen);

        await tool.InvokeAsync(Args("""{"row":4}"""), Ctx(), default);
        Assert.Equal(4, seen);
    }

    [Fact]
    public async Task ShowObservationDetail_RequiresAPublisherId()
    {
        var tool = new ShowObservationDetailTool(id => Task.FromResult(new SearchRowDetailOutcome(true, null, id)));
        Assert.Contains("publisherId is required", Failure(await tool.InvokeAsync(Args("""{"publisherId":"  "}"""), Ctx(), default)));
    }

    [Fact]
    public async Task RemoveRecentSearch_ReportsAMissWithoutFailing()
    {
        // Not finding an entry is an ANSWER, not an error: an agent tidying up should be told the rail
        // no longer holds it, not handed a failure it has to interpret.
        var tool = new RemoveRecentSearchTool(_ => Task.FromResult(new SearchRecentRemoved(false, null, 3, "no recent search matching 'x'")));
        var removed = Payload<SearchRecentRemoved>(await tool.InvokeAsync(Args("""{"match":"x"}"""), Ctx(), default));

        Assert.False(removed.Removed);
        Assert.Equal(3, removed.Remaining);
    }

    // ── An unreachable page ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AClosedSearchPageIsAnAnswerNotAFailure()
    {
        // The page is built on first use, and a call that arrives before it exists still has to say
        // something an agent can act on. Available:false with a reason is that; a FailedResult would
        // read as "the tool is broken".
        var tool = new GetSearchFormTool(() => Task.FromResult(SearchFormView.Unavailable("the Search page is not available")));
        var view = Payload<SearchFormView>(await tool.InvokeAsync(Args("{}"), Ctx(), default));

        Assert.False(view.Available);
        Assert.Contains("not available", view.Message);
    }

    // ── Argument conversion ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("\"M31\"", "M31")]
    [InlineData("0.5", "0.5")]
    [InlineData("true", "true")]
    [InlineData("false", "false")]
    [InlineData("null", null)]
    public void ToFormValue_AcceptsWhatAFormFieldCanHold(string json, string? expected)
        => Assert.Equal(expected, SearchToolArgs.ToFormValue("target", JsonDocument.Parse(json).RootElement));

    [Fact]
    public void DidYouMean_PrefersNearMissesAndFallsBackToWhatExists()
    {
        Assert.Equal(["CFHT", "CFHTMEGAPIPE"], SearchToolArgs.DidYouMean("CFHT", ["CFHT", "CFHTMEGAPIPE", "JWST"]));
        // Nothing resembles it — the available values are still more use than silence.
        Assert.Equal(["CFHT", "JWST"], SearchToolArgs.DidYouMean("zzz", ["CFHT", "JWST"]));
        Assert.Empty(SearchToolArgs.DidYouMean("anything", []));
    }
}
