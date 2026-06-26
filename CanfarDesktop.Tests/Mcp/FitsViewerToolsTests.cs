using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Mcp.Wire;
using CanfarDesktop.Services.Fits;

namespace CanfarDesktop.Tests.Mcp;

public class FitsViewerToolsTests
{
    private static readonly McpToolContext Ctx = McpToolContext.ForExternal("c1", Guid.Empty);
    private static JsonValue Args(string json) => JsonValue.Parse(json);
    private static JsonElement Json(ToolResult r) => JsonDocument.Parse(Assert.IsType<DataResult>(r).Json).RootElement;

    private static FitsViewState SampleState() => new(
        Loaded: true, FileName: "m51.fits", Width: 1024, Height: 1024,
        Stretch: "Asinh", Colormap: "Viridis", MinCut: -0.5, MaxCut: 12.3,
        ZoomPercent: 150, NorthUp: true, HasWcs: true,
        CrosshairPlaced: true, CrosshairRa: 202.47, CrosshairDec: 47.2);

    // ── set_fits_view ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SetFitsView_MapsArgs_ReturnsState()
    {
        FitsViewArgs? seen = null;
        var tool = new SetFitsViewTool(a => { seen = a; return Task.FromResult<FitsViewState?>(SampleState()); });
        var doc = Json(await tool.InvokeAsync(Args("""
            {"stretch":"log","colormap":"heat","minCut":1.5,"maxCut":99.0,"zoomPercent":200,"northUp":true,"reset":false,"clearCrosshair":true}
            """), Ctx, default));
        Assert.Equal("log", seen!.Stretch);
        Assert.Equal("heat", seen.Colormap);
        Assert.Equal(1.5, seen.MinCut);
        Assert.Equal(99.0, seen.MaxCut);
        Assert.Equal(200, seen.ZoomPercent);
        Assert.True(seen.NorthUp);
        Assert.False(seen.Reset);
        Assert.True(seen.ClearCrosshair);
        Assert.Equal("m51.fits", doc.GetProperty("fileName").GetString());
    }

    [Fact]
    public void SetFitsView_IsViewStateVerb()
        => Assert.Equal(McpVerbClass.ViewState,
            new SetFitsViewTool(_ => Task.FromResult<FitsViewState?>(null)).VerbClass);

    // ── get_fits_view ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetFitsView_ReturnsState()
    {
        var tool = new GetFitsViewTool(() => Task.FromResult<FitsViewState?>(SampleState()));
        var doc = Json(await tool.InvokeAsync(Args("""{}"""), Ctx, default));
        Assert.Equal(1024, doc.GetProperty("width").GetInt32());
        Assert.Equal("Viridis", doc.GetProperty("colormap").GetString());
        Assert.Equal(150, doc.GetProperty("zoomPercent").GetDouble());
        Assert.True(doc.GetProperty("northUp").GetBoolean());
        Assert.True(doc.GetProperty("hasWcs").GetBoolean());
    }

    // ── probe_fits_pixel ──────────────────────────────────────────────────────

    [Fact]
    public async Task ProbeFitsPixel_InvokesClosure()
    {
        (int x, int y)? seen = null;
        var tool = new ProbeFitsPixelTool((x, y) =>
        {
            seen = (x, y);
            return Task.FromResult<FitsPixelResult?>(new(x, y, 42.5, true, 202.4, 47.2));
        });
        var doc = Json(await tool.InvokeAsync(Args("""{"x":100,"y":200}"""), Ctx, default));
        Assert.Equal((100, 200), seen);
        Assert.Equal(42.5, doc.GetProperty("value").GetDouble());
        Assert.Equal(202.4, doc.GetProperty("ra").GetDouble());
    }

    [Fact]
    public async Task ProbeFitsPixel_MissingCoords_InvalidArgument()
    {
        var tool = new ProbeFitsPixelTool((_, _) => Task.FromResult<FitsPixelResult?>(null));
        var r = await tool.InvokeAsync(Args("""{"x":5}"""), Ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(r).Reason);
    }

    // ── fits_goto_coordinate ──────────────────────────────────────────────────

    [Fact]
    public async Task FitsGoto_InvokesClosure_PassesRaDec()
    {
        (double ra, double dec)? seen = null;
        var tool = new FitsGotoCoordinateTool((ra, dec) =>
        {
            seen = (ra, dec);
            return Task.FromResult(new FitsGotoOutcome(true, ra, dec, null));
        });
        var doc = Json(await tool.InvokeAsync(Args("""{"ra":202.47,"dec":47.2}"""), Ctx, default));
        Assert.Equal((202.47, 47.2), seen);
        Assert.True(doc.GetProperty("moved").GetBoolean());
    }

    [Fact]
    public async Task FitsGoto_MissingCoords_InvalidArgument()
    {
        var tool = new FitsGotoCoordinateTool((_, _) => Task.FromResult(new FitsGotoOutcome(false, 0, 0, null)));
        var r = await tool.InvokeAsync(Args("""{"ra":202.47}"""), Ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(r).Reason);
    }

    [Fact]
    public void FitsGoto_IsViewStateVerb()
        => Assert.Equal(McpVerbClass.ViewState,
            new FitsGotoCoordinateTool((_, _) => Task.FromResult(new FitsGotoOutcome(false, 0, 0, null))).VerbClass);
}
