using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Builtin;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

/// <summary>
/// The map: list_apps and search_tools.
///
/// tools/list is every schema the server has, and most of it is irrelevant to any one task. These help
/// an agent CHOOSE — they do not, and cannot, shrink what a client loads on connect.
/// </summary>
public class ToolMapToolsTests
{
    private static JsonValue Args(string json) => JsonValue.Parse(json);
    private static McpToolContext Ctx() => McpToolContext.ForExternal("c1", Guid.NewGuid());

    private static T Payload<T>(ToolResult result)
        => JsonSerializer.Deserialize<T>(Assert.IsType<DataResult>(result).Json, McpJson.Options)!;

    private static string Failure(ToolResult result) => Assert.IsType<FailedResult>(result).Reason.Description;

    /// <summary>Real tool names, so the categories they land in are the real ones too.</summary>
    private static readonly string[] LiveNames =
        ["get_auth_state", "search_observations", "annotate_fits", "open_fits_file", "get_cell_image", "save_query"];

    private static ToolDescriptor Descriptor(string name, string description)
        => ToolDescriptor.WithStaticSchema(name, description, """{"type":"object","properties":{},"additionalProperties":false}""");

    // ── list_apps ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheAppsAreListedWithHowManyToolsEachHas()
    {
        var tool = new ListAppsTool(() => LiveNames);

        var output = Payload<ListAppsTool.Output>(await tool.InvokeAsync(Args("{}"), Ctx(), default));

        Assert.Equal(LiveNames.Length, output.ToolCount);
        Assert.True(output.AppCount > 1);
        Assert.All(output.Apps, a => Assert.True(a.ToolCount > 0));
        Assert.Equal(LiveNames.Length, output.Apps.Sum(a => a.ToolCount));
    }

    /// <summary>
    /// An area with no live tools is left out. The taxonomy is a superset — it keeps names from the
    /// other platforms so a tool is never silently uncategorised — and listing an area an agent cannot
    /// then describe would be a map with roads that go nowhere.
    /// </summary>
    [Fact]
    public async Task AnAreaWithNoLiveToolsIsNotListed()
    {
        var tool = new ListAppsTool(() => ["get_auth_state"]);

        var output = Payload<ListAppsTool.Output>(await tool.InvokeAsync(Args("{}"), Ctx(), default));

        Assert.Single(output.Apps);
        Assert.Equal(1, output.Apps[0].ToolCount);
    }

    [Fact]
    public async Task EveryAppCarriesWhatItIsFor()
    {
        var tool = new ListAppsTool(() => LiveNames);

        var output = Payload<ListAppsTool.Output>(await tool.InvokeAsync(Args("{}"), Ctx(), default));

        Assert.All(output.Apps, a =>
        {
            Assert.NotEmpty(a.Id);
            Assert.NotEmpty(a.Title);
            Assert.NotEmpty(a.Summary);
        });
    }

    // ── search_tools ────────────────────────────────────────────────────────────────────────────

    private static SearchToolsTool Search() => new(() =>
    [
        Descriptor("annotate_fits", "Draw a mark on a FITS image. Circles and boxes."),
        Descriptor("export_fits_figure", "Save a publication figure of the open FITS image."),
        Descriptor("search_observations", "Search CADC observations by cone or ADQL."),
    ]);

    [Fact]
    public async Task AToolIsFoundByItsName()
    {
        var output = Payload<SearchToolsTool.Output>(
            await Search().InvokeAsync(Args("""{"query":"annotate"}"""), Ctx(), default));

        Assert.Equal("annotate_fits", output.Tools[0].Name);
        Assert.NotEmpty(output.Tools[0].App);
    }

    /// <summary>
    /// A name match beats a description match: someone typing "figure" wants export_fits_figure before
    /// every tool whose prose happens to mention figures.
    /// </summary>
    [Fact]
    public async Task ANameMatchOutranksADescriptionMatch()
    {
        var output = Payload<SearchToolsTool.Output>(
            await Search().InvokeAsync(Args("""{"query":"figure"}"""), Ctx(), default));

        Assert.Equal("export_fits_figure", output.Tools[0].Name);
    }

    [Fact]
    public async Task AToolIsAlsoFoundByWhatItDoes()
    {
        var output = Payload<SearchToolsTool.Output>(
            await Search().InvokeAsync(Args("""{"query":"cone"}"""), Ctx(), default));

        Assert.Equal("search_observations", Assert.Single(output.Tools).Name);
    }

    /// <summary>
    /// The first sentence only. A search result is for CHOOSING between tools; repeating the whole
    /// description would make the map as long as the territory.
    /// </summary>
    [Fact]
    public async Task AResultCarriesASummaryRatherThanTheWholeDescription()
    {
        var output = Payload<SearchToolsTool.Output>(
            await Search().InvokeAsync(Args("""{"query":"annotate"}"""), Ctx(), default));

        Assert.Equal("Draw a mark on a FITS image.", output.Tools[0].Summary);
    }

    [Fact]
    public async Task ASearchCanBeNarrowedToOneArea()
    {
        var all = Payload<SearchToolsTool.Output>(
            await Search().InvokeAsync(Args("""{"query":"fits"}"""), Ctx(), default));

        var narrowed = Payload<SearchToolsTool.Output>(
            await Search().InvokeAsync(Args($$"""{"query":"fits","app":"{{all.Tools[0].AppId}}"}"""), Ctx(), default));

        Assert.All(narrowed.Tools, t => Assert.Equal(all.Tools[0].AppId, t.AppId));
        Assert.True(narrowed.Count <= all.Count);
    }

    /// <summary>Tools as they describe themselves, for queries written the way an agent writes them.</summary>
    private static SearchToolsTool RealisticSearch() => new(() =>
    [
        Descriptor("probe_cube_spectrum", "Extract the spectrum (flux per channel) at a 0-based native spatial pixel (x, y) of the loaded cube."),
        Descriptor("get_cube_view", "Read the cube viewer's state: mode, channel, colormap and window."),
        Descriptor("probe_fits_pixel", "Read the value at a pixel of the open FITS image, with its RA/Dec."),
        Descriptor("run_code", "Run a short Python or Bash snippet on a warm remote CANFAR compute session."),
        Descriptor("get_compute_state", "Whether remote compute (run_code) is set up, and the state of its session on the user's CANFAR account."),
    ]);

    /// <summary>
    /// A query is the words in it. The whole query was matched as one phrase, so "cube spectrum" found
    /// nothing although probe_cube_spectrum carries both words — and agents write several words.
    /// </summary>
    [Theory]
    [InlineData("cube spectrum", "probe_cube_spectrum")]
    [InlineData("measure the spectrum at a pixel of a cube", "probe_cube_spectrum")]
    [InlineData("run python on canfar", "run_code")]
    [InlineData("pixel value in a fits image", "probe_fits_pixel")]
    public async Task AQueryOfSeveralWordsFindsTheToolThatHasThem(string query, string expected)
    {
        var output = Payload<SearchToolsTool.Output>(
            await RealisticSearch().InvokeAsync(Args($$"""{"query":"{{query}}"}"""), Ctx(), default));

        Assert.Equal(expected, output.Tools[0].Name);
    }

    /// <summary>One shared word is not a match when the query says more: "cube spectrum" is not a request for every cube tool.</summary>
    [Fact]
    public async Task AToolWithOnlyOneOfSeveralWordsIsNotOffered()
    {
        var output = Payload<SearchToolsTool.Output>(
            await RealisticSearch().InvokeAsync(Args("""{"query":"cube spectrum"}"""), Ctx(), default));

        Assert.DoesNotContain(output.Tools, t => t.Name == "get_cube_view");
    }

    [Fact]
    public async Task NothingMatchingSaysWhereToLookInstead()
    {
        var output = Payload<SearchToolsTool.Output>(
            await Search().InvokeAsync(Args("""{"query":"zzzznope"}"""), Ctx(), default));

        Assert.Equal(0, output.Count);
        Assert.Contains("list_apps", output.Message);
    }

    [Fact]
    public async Task AnEmptyQueryIsRefused()
        => Assert.Contains("query is required", Failure(await Search().InvokeAsync(Args("""{"query":"  "}"""), Ctx(), default)));

    [Fact]
    public void BothAreReadsAndAgentSafe()
    {
        IMcpTool[] tools = [new ListAppsTool(() => []), Search()];

        Assert.All(tools, t => Assert.Equal(McpVerbClass.Read, t.VerbClass));
        Assert.All(tools, t => Assert.True(t.AgentSafe));
    }
}
