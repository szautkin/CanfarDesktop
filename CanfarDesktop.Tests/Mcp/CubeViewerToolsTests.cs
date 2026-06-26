using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Mcp.Wire;
using CanfarDesktop.Services.CubeViewer;

namespace CanfarDesktop.Tests.Mcp;

public class CubeViewerToolsTests
{
    private static readonly McpToolContext Ctx = McpToolContext.ForExternal("c1", Guid.Empty);
    private static JsonValue Args(string json) => JsonValue.Parse(json);
    private static JsonElement Json(ToolResult r) => JsonDocument.Parse(Assert.IsType<DataResult>(r).Json).RootElement;

    private static CubeViewState SampleState() => new(
        true, "cube.fits", "M51", 256, 256, 64, "Volume", 32, "230.5 GHz",
        "Inferno", "Asinh", "Emission", 0.0, 1.0, "Jy/beam", -0.1, 5.2);

    // ── open_cube ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task OpenCube_InvokesClosure_ReturnsDims()
    {
        string? seen = null;
        var tool = new OpenCubeTool(t => { seen = t; return Task.FromResult(new CubeOpenOutcome(true, t, 256, 256, 64, null)); });
        var doc = Json(await tool.InvokeAsync(Args("""{"path":"/tmp/c.fits"}"""), Ctx, default));
        Assert.Equal("/tmp/c.fits", seen);
        Assert.True(doc.GetProperty("opened").GetBoolean());
        Assert.Equal(64, doc.GetProperty("nz").GetInt32());
    }

    [Fact]
    public async Task OpenCube_FallsBackToObservationId()
    {
        string? seen = null;
        var tool = new OpenCubeTool(t => { seen = t; return Task.FromResult(new CubeOpenOutcome(true, t, 1, 1, 2, null)); });
        await tool.InvokeAsync(Args("""{"observationId":"obs-7"}"""), Ctx, default);
        Assert.Equal("obs-7", seen);
    }

    [Fact]
    public async Task OpenCube_NoTarget_InvalidArgument()
    {
        var tool = new OpenCubeTool(_ => Task.FromResult(new CubeOpenOutcome(false, null, 0, 0, 0, null)));
        var r = await tool.InvokeAsync(Args("""{}"""), Ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(r).Reason);
    }

    [Fact]
    public void OpenCube_IsViewStateVerb()
        => Assert.Equal(McpVerbClass.ViewState, new OpenCubeTool(_ => Task.FromResult(new CubeOpenOutcome(true, "x", 0, 0, 0, null))).VerbClass);

    // ── set_cube_view ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SetCubeView_AppliesArgs_ReturnsState()
    {
        CubeViewArgs? seen = null;
        var tool = new SetCubeViewTool(a => { seen = a; return Task.FromResult<CubeViewState?>(SampleState()); });
        var doc = Json(await tool.InvokeAsync(Args("""{"mode":"slice","channel":10,"colormap":"viridis"}"""), Ctx, default));
        Assert.Equal("slice", seen!.Mode);
        Assert.Equal(10, seen.Channel);
        Assert.Equal("viridis", seen.Colormap);
        Assert.Equal("M51", doc.GetProperty("object").GetString());
    }

    [Fact]
    public async Task SetCubeView_NegativeChannel_InvalidArgument()
    {
        var tool = new SetCubeViewTool(_ => Task.FromResult<CubeViewState?>(null));
        var r = await tool.InvokeAsync(Args("""{"channel":-1}"""), Ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(r).Reason);
    }

    // ── get_cube_view ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetCubeView_ReturnsState()
    {
        var tool = new GetCubeViewTool(() => Task.FromResult<CubeViewState?>(SampleState()));
        var doc = Json(await tool.InvokeAsync(Args("""{}"""), Ctx, default));
        Assert.Equal(256, doc.GetProperty("nx").GetInt32());
        Assert.Equal("Inferno", doc.GetProperty("colormap").GetString());
    }

    // ── probe_cube_spectrum ───────────────────────────────────────────────────

    [Fact]
    public async Task ProbeCubeSpectrum_InvokesClosure()
    {
        (int x, int y)? seen = null;
        var tool = new ProbeCubeSpectrumTool((x, y) =>
        {
            seen = (x, y);
            return Task.FromResult<CubeSpectrumResult?>(new(x, y, new double[] { 0, 1 }, new double[] { 2, 3 }, "Jy", "GHz"));
        });
        var doc = Json(await tool.InvokeAsync(Args("""{"x":5,"y":7}"""), Ctx, default));
        Assert.Equal((5, 7), seen);
        Assert.Equal(2, doc.GetProperty("flux")[0].GetDouble());
    }

    [Fact]
    public async Task ProbeCubeSpectrum_MissingCoords_InvalidArgument()
    {
        var tool = new ProbeCubeSpectrumTool((_, _) => Task.FromResult<CubeSpectrumResult?>(null));
        var r = await tool.InvokeAsync(Args("""{"x":5}"""), Ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(r).Reason);
    }

    // ── export_cube_figure ────────────────────────────────────────────────────

    [Fact]
    public async Task ExportCubeFigure_InfersFormat_AndPassesArgs()
    {
        string? path = null, fmt = null;
        int scale = 0;
        bool dark = true;
        var tool = new ExportCubeFigureTool((p, f, s, d) =>
        {
            path = p; fmt = f; scale = s; dark = d;
            return Task.FromResult(new CubeExportOutcome(true, p, null));
        });
        var doc = Json(await tool.InvokeAsync(Args("""{"path":"/tmp/fig.pdf","scale":4,"theme":"light"}"""), Ctx, default));
        Assert.Equal("/tmp/fig.pdf", path);
        Assert.Equal("pdf", fmt);   // inferred from the .pdf extension
        Assert.Equal(4, scale);
        Assert.False(dark);         // theme=light
        Assert.True(doc.GetProperty("exported").GetBoolean());
    }

    [Fact]
    public async Task ExportCubeFigure_NoPath_InvalidArgument()
    {
        var tool = new ExportCubeFigureTool((_, _, _, _) => Task.FromResult(new CubeExportOutcome(false, null, null)));
        var r = await tool.InvokeAsync(Args("""{}"""), Ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(r).Reason);
    }
}
