using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

public class ViewStateWriteToolsTests
{
    private static readonly McpToolContext Ctx = McpToolContext.ForExternal("c1", Guid.Empty);
    private static JsonValue Args(string json) => JsonValue.Parse(json);
    private static JsonElement Json(ToolResult result) => JsonDocument.Parse(Assert.IsType<DataResult>(result).Json).RootElement;

    // ── navigate_to ───────────────────────────────────────────────────────────

    [Fact]
    public async Task NavigateTo_InvokesClosure_ReturnsOutcome()
    {
        string? seen = null;
        var tool = new NavigateToTool(mode => { seen = mode; return Task.FromResult(new NavigationOutcome(true, mode, "Search")); });

        var doc = Json(await tool.InvokeAsync(Args("""{"mode":"search"}"""), Ctx, default));

        Assert.Equal("search", seen);
        Assert.True(doc.GetProperty("navigated").GetBoolean());
        Assert.Equal("Search", doc.GetProperty("modeTitle").GetString());
    }

    [Fact]
    public async Task NavigateTo_EmptyMode_InvalidArgument()
    {
        var tool = new NavigateToTool(mode => Task.FromResult(new NavigationOutcome(true, mode, mode)));
        var result = await tool.InvokeAsync(Args("""{"mode":"   "}"""), Ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
    }

    [Fact]
    public void NavigateTo_IsViewStateVerb()
        => Assert.Equal(McpVerbClass.ViewState, new NavigateToTool(_ => Task.FromResult(new NavigationOutcome(true, "x", "x"))).VerbClass);

    // ── set_search_focus ──────────────────────────────────────────────────────

    [Fact]
    public async Task SetSearchFocus_Applies()
    {
        (double ra, double dec)? applied = null;
        var tool = new SetSearchFocusTool((ra, dec) => { applied = (ra, dec); return Task.CompletedTask; });

        var doc = Json(await tool.InvokeAsync(Args("""{"raDeg":180.5,"decDeg":-12.25}"""), Ctx, default));

        Assert.Equal((180.5, -12.25), applied);
        Assert.True(doc.GetProperty("applied").GetBoolean());
        Assert.Equal(180.5, doc.GetProperty("raDeg").GetDouble());
    }

    [Theory]
    [InlineData("""{"raDeg":400,"decDeg":0}""")]   // RA out of range
    [InlineData("""{"raDeg":10,"decDeg":120}""")]  // Dec out of range
    public async Task SetSearchFocus_OutOfRange_InvalidArgument(string json)
    {
        var tool = new SetSearchFocusTool((_, _) => Task.CompletedTask);
        var result = await tool.InvokeAsync(Args(json), Ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
    }

    [Fact]
    public async Task SetSearchFocus_MissingFields_InvalidArgument()
    {
        var tool = new SetSearchFocusTool((_, _) => Task.CompletedTask);
        var result = await tool.InvokeAsync(Args("""{"raDeg":10}"""), Ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
    }

    // ── open_fits_file ────────────────────────────────────────────────────────

    [Fact]
    public async Task OpenFits_InvokesClosure_ReturnsOutcome()
    {
        string? seen = null;
        var tool = new OpenFitsFileTool(id => { seen = id; return Task.FromResult(new OpenFitsOutcome(true, id, "/tmp/a.fits", null)); });

        var doc = Json(await tool.InvokeAsync(Args("""{"observationId":"obs-1"}"""), Ctx, default));

        Assert.Equal("obs-1", seen);
        Assert.True(doc.GetProperty("opened").GetBoolean());
        Assert.Equal("/tmp/a.fits", doc.GetProperty("localPath").GetString());
    }

    [Fact]
    public async Task OpenFits_NotDownloaded_ReportsNotOpened()
    {
        var tool = new OpenFitsFileTool(id => Task.FromResult(new OpenFitsOutcome(false, id, null, "not downloaded yet")));
        var doc = Json(await tool.InvokeAsync(Args("""{"observationId":"obs-1"}"""), Ctx, default));
        Assert.False(doc.GetProperty("opened").GetBoolean());
    }

    [Fact]
    public async Task OpenFits_AcceptsLocalPath_PathWins()
    {
        // UI-parity with the file picker (and open_cube): a local path opens directly.
        string? seen = null;
        var tool = new OpenFitsFileTool(t => { seen = t; return Task.FromResult(new OpenFitsOutcome(true, t, t, null)); });
        await tool.InvokeAsync(Args("""{"path":"C:/data/m51.fits"}"""), Ctx, default);
        Assert.Equal("C:/data/m51.fits", seen);
        await tool.InvokeAsync(Args("""{"path":"C:/data/a.fits","observationId":"obs-1"}"""), Ctx, default);
        Assert.Equal("C:/data/a.fits", seen); // path takes precedence when both are given
    }

    [Fact]
    public async Task OpenFits_NoTarget_InvalidArgument()
    {
        var tool = new OpenFitsFileTool(_ => Task.FromResult(new OpenFitsOutcome(false, "", null, null)));
        var r = await tool.InvokeAsync(Args("""{}"""), Ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(r).Reason);
    }

    [Fact]
    public void OpenFits_IsViewStateVerb()
        => Assert.Equal(McpVerbClass.ViewState, new OpenFitsFileTool(_ => Task.FromResult(new OpenFitsOutcome(true, "x", null, null))).VerbClass);
}
