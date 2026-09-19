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
        "Inferno", "Asinh", "Emission", 0.0, 1.0, "Jy/beam", -0.1, 5.2,
        Azimuth: 0.7, Elevation: 0.5, Distance: 2.6, Density: 1.0, SpectralScale: 1.5, Steps: 384,
        Background: "dark", ShowSlicePlane: true, ShowCaptions: true, AutoOrbit: false, Playing: false);

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

    [Fact]
    public async Task SetCubeView_NoCubeOpen_TargetNotResolved()
    {
        var tool = new SetCubeViewTool(_ => Task.FromResult<CubeViewState?>(null));
        var r = await tool.InvokeAsync(Args("""{"mode":"slice"}"""), Ctx, default);
        Assert.IsType<TargetNotResolved>(Assert.IsType<FailedResult>(r).Reason);
    }

    [Fact]
    public async Task SetCubeView_MapsCameraVolumeTogglesAndPlayback()
    {
        CubeViewArgs? seen = null;
        var tool = new SetCubeViewTool(a => { seen = a; return Task.FromResult<CubeViewState?>(SampleState()); });
        await tool.InvokeAsync(Args("""
            {"azimuth":1.2,"elevation":0.3,"distance":3.5,"density":2.0,"spectralScale":2.5,"steps":512,
             "background":"black","showSlicePlane":false,"showCaptions":false,"autoOrbit":true,"playing":true,"resetCamera":true}
            """), Ctx, default);
        Assert.Equal(1.2, seen!.Azimuth);
        Assert.Equal(0.3, seen.Elevation);
        Assert.Equal(3.5, seen.Distance);
        Assert.Equal(2.0, seen.Density);
        Assert.Equal(2.5, seen.SpectralScale);
        Assert.Equal(512, seen.Steps);
        Assert.Equal("black", seen.Background);
        Assert.False(seen.ShowSlicePlane);
        Assert.False(seen.ShowCaptions);
        Assert.True(seen.AutoOrbit);
        Assert.True(seen.Playing);
        Assert.True(seen.ResetCamera);
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

    [Fact]
    public async Task GetCubeView_ReturnsCameraVolumeAndToggleState()
    {
        var tool = new GetCubeViewTool(() => Task.FromResult<CubeViewState?>(SampleState()));
        var doc = Json(await tool.InvokeAsync(Args("""{}"""), Ctx, default));
        Assert.Equal(2.6, doc.GetProperty("distance").GetDouble());
        Assert.Equal(384, doc.GetProperty("steps").GetInt32());
        Assert.Equal("dark", doc.GetProperty("background").GetString());
        Assert.True(doc.GetProperty("showSlicePlane").GetBoolean());
        Assert.False(doc.GetProperty("playing").GetBoolean());
    }

    // ── probe_cube_spectrum ───────────────────────────────────────────────────

    private static CubeSpectrumProbe OkProbe(int x, int y, double?[] flux)
        => new(CubeProbeStatus.Ok, new(x, y, new double[] { 0, 1 }, flux, "Jy", "GHz"), 10, 10);

    [Fact]
    public async Task ProbeCubeSpectrum_InvokesClosure()
    {
        (int x, int y)? seen = null;
        var tool = new ProbeCubeSpectrumTool((x, y) =>
        {
            seen = (x, y);
            return Task.FromResult<CubeSpectrumProbe?>(OkProbe(x, y, new double?[] { 2, 3 }));
        });
        var doc = Json(await tool.InvokeAsync(Args("""{"x":5,"y":7}"""), Ctx, default));
        Assert.Equal((5, 7), seen);
        Assert.Equal(2, doc.GetProperty("flux")[0].GetDouble());
    }

    [Fact]
    public async Task ProbeCubeSpectrum_MissingCoords_InvalidArgument()
    {
        var tool = new ProbeCubeSpectrumTool((_, _) => Task.FromResult<CubeSpectrumProbe?>(null));
        var r = await tool.InvokeAsync(Args("""{"x":5}"""), Ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(r).Reason);
    }

    [Fact]
    public async Task ProbeCubeSpectrum_BlankedChannels_SerializeAsNull()
    {
        // QA F10a: masked (NaN) voxels crashed the JSON serializer. They must arrive as null flux
        // entries + a blankedChannels count, in standard-compliant JSON.
        var tool = new ProbeCubeSpectrumTool((x, y) => Task.FromResult<CubeSpectrumProbe?>(
            new(CubeProbeStatus.Ok,
                new(x, y, new double[] { 0, 1 }, new double?[] { 2.5, null }, "Jy", "GHz", BlankedChannels: 1))));
        var doc = Json(await tool.InvokeAsync(Args("""{"x":0,"y":0}"""), Ctx, default));
        Assert.Equal(2.5, doc.GetProperty("flux")[0].GetDouble());
        Assert.Equal(JsonValueKind.Null, doc.GetProperty("flux")[1].ValueKind);
        Assert.Equal(1, doc.GetProperty("blankedChannels").GetInt32());
    }

    [Fact]
    public async Task ProbeCubeSpectrum_NoCube_TargetNotResolved()
    {
        // QA F10b: "no cube loaded" used to be an undiagnosable null — it must be a typed error.
        var tool = new ProbeCubeSpectrumTool((_, _) => Task.FromResult<CubeSpectrumProbe?>(new(CubeProbeStatus.NoCube, null)));
        var r = await tool.InvokeAsync(Args("""{"x":1,"y":1}"""), Ctx, default);
        Assert.IsType<TargetNotResolved>(Assert.IsType<FailedResult>(r).Reason);
    }

    [Fact]
    public async Task ProbeCubeSpectrum_OutOfRange_InvalidArgument_ReportsDims()
    {
        // QA F10b: out-of-range must be a typed error carrying the valid pixel grid.
        var tool = new ProbeCubeSpectrumTool((_, _) => Task.FromResult<CubeSpectrumProbe?>(
            new(CubeProbeStatus.OutOfRange, null, 720, 360)));
        var r = await tool.InvokeAsync(Args("""{"x":800,"y":400}"""), Ctx, default);
        var reason = Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(r).Reason);
        Assert.Contains("720", reason.Detail);
        Assert.Contains("360", reason.Detail);
    }

    [Fact]
    public async Task ProbeCubeSpectrum_DispatchFailure_BackendError()
    {
        // A failed UI dispatch is the only case that yields null — it must not masquerade as "no cube".
        var tool = new ProbeCubeSpectrumTool((_, _) => Task.FromResult<CubeSpectrumProbe?>(null));
        var r = await tool.InvokeAsync(Args("""{"x":1,"y":1}"""), Ctx, default);
        Assert.IsType<BackendError>(Assert.IsType<FailedResult>(r).Reason);
    }

    // ── export_cube_figure ────────────────────────────────────────────────────

    [Fact]
    public async Task ExportCubeFigure_InfersFormat_AndPassesArgs()
    {
        CubeExportRequest? seen = null;
        var tool = new ExportCubeFigureTool(req =>
        {
            seen = req;
            return Task.FromResult(new CubeExportOutcome(true, req.Path, null));
        });
        var doc = Json(await tool.InvokeAsync(Args("""{"path":"/tmp/fig.pdf","scale":4,"theme":"light"}"""), Ctx, default));
        Assert.Equal("/tmp/fig.pdf", seen!.Path);
        Assert.Equal("pdf", seen.Format);   // inferred from the .pdf extension
        Assert.Equal(4, seen.Scale);
        Assert.False(seen.Dark);            // theme=light
        Assert.True(doc.GetProperty("exported").GetBoolean());
        // Style defaults match the export dialog's initial state.
        Assert.Equal("sans", seen.Font);
        Assert.Equal("auto", seen.TextColor);
        Assert.Equal(1.0, seen.TextScale);
        Assert.True(seen.Annotate);
        Assert.False(seen.Transparent);
    }

    [Fact]
    public async Task ExportCubeFigure_PassesStyleOptions()
    {
        // UI-parity: the export dialog's five style controls must be reachable from the tool.
        CubeExportRequest? seen = null;
        var tool = new ExportCubeFigureTool(req =>
        {
            seen = req;
            return Task.FromResult(new CubeExportOutcome(true, req.Path, null));
        });
        await tool.InvokeAsync(Args("""
            {"path":"/tmp/f.png","font":"mono","textColor":"amber","textScale":1.3,"annotate":false,"transparent":true}
            """), Ctx, default);
        Assert.Equal("mono", seen!.Font);
        Assert.Equal("amber", seen.TextColor);
        Assert.Equal(1.3, seen.TextScale);
        Assert.False(seen.Annotate);
        Assert.True(seen.Transparent);
    }

    [Fact]
    public async Task ExportCubeFigure_InvalidFont_InvalidArgument()
    {
        var tool = new ExportCubeFigureTool(_ => Task.FromResult(new CubeExportOutcome(false, null, null)));
        var r = await tool.InvokeAsync(Args("""{"path":"/tmp/f.png","font":"comic-sans"}"""), Ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(r).Reason);
    }

    [Fact]
    public async Task ExportCubeFigure_NoPath_InvalidArgument()
    {
        var tool = new ExportCubeFigureTool(_ => Task.FromResult(new CubeExportOutcome(false, null, null)));
        var r = await tool.InvokeAsync(Args("""{}"""), Ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(r).Reason);
    }

    // ── set_cube_view (slice navigation + window presets) ─────────────────────

    [Fact]
    public async Task SetCubeView_MapsSliceNavAndWindowPreset()
    {
        CubeViewArgs? seen = null;
        var tool = new SetCubeViewTool(a => { seen = a; return Task.FromResult<CubeViewState?>(SampleState()); });
        await tool.InvokeAsync(Args("""
            {"windowPreset":"p99","sliceZoom":6.5,"sliceCenterX":360,"sliceCenterY":180,"resetSliceView":false}
            """), Ctx, default);
        Assert.Equal("p99", seen!.WindowPreset);
        Assert.Equal(6.5, seen.SliceZoom);
        Assert.Equal(360, seen.SliceCenterX);
        Assert.Equal(180, seen.SliceCenterY);
        Assert.False(seen.ResetSliceView);
    }

    [Fact]
    public async Task SetCubeView_BadWindowPreset_InvalidArgument()
    {
        var tool = new SetCubeViewTool(_ => Task.FromResult<CubeViewState?>(SampleState()));
        var r = await tool.InvokeAsync(Args("""{"windowPreset":"p50"}"""), Ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(r).Reason);
    }

    // ── show_cube_spectrum ────────────────────────────────────────────────────

    [Fact]
    public async Task ShowCubeSpectrum_OpensPanel_ReturnsSpectrum()
    {
        (int x, int y)? seen = null;
        var tool = new ShowCubeSpectrumTool(
            (x, y) => { seen = (x, y); return Task.FromResult<CubeSpectrumProbe?>(OkProbe(x, y, new double?[] { 1, 2 })); },
            () => Task.FromResult(true));
        var doc = Json(await tool.InvokeAsync(Args("""{"x":12,"y":34}"""), Ctx, default));
        Assert.Equal((12, 34), seen);
        Assert.True(doc.GetProperty("panelOpen").GetBoolean());
        Assert.Equal(1, doc.GetProperty("spectrum").GetProperty("flux")[0].GetDouble());
    }

    [Fact]
    public async Task ShowCubeSpectrum_Close_DismissesPanel()
    {
        bool closed = false;
        var tool = new ShowCubeSpectrumTool(
            (_, _) => Task.FromResult<CubeSpectrumProbe?>(null),
            () => { closed = true; return Task.FromResult(true); });
        var doc = Json(await tool.InvokeAsync(Args("""{"close":true}"""), Ctx, default));
        Assert.True(closed);
        Assert.False(doc.GetProperty("panelOpen").GetBoolean());
    }

    [Fact]
    public async Task ShowCubeSpectrum_MissingCoords_InvalidArgument()
    {
        var tool = new ShowCubeSpectrumTool(
            (_, _) => Task.FromResult<CubeSpectrumProbe?>(null), () => Task.FromResult(true));
        var r = await tool.InvokeAsync(Args("""{}"""), Ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(r).Reason);
    }

    [Fact]
    public async Task ShowCubeSpectrum_NoCube_TargetNotResolved()
    {
        var tool = new ShowCubeSpectrumTool(
            (_, _) => Task.FromResult<CubeSpectrumProbe?>(new(CubeProbeStatus.NoCube, null)),
            () => Task.FromResult(true));
        var r = await tool.InvokeAsync(Args("""{"x":1,"y":1}"""), Ctx, default);
        Assert.IsType<TargetNotResolved>(Assert.IsType<FailedResult>(r).Reason);
    }

    // ── set_cube_transfer ─────────────────────────────────────────────────────

    [Fact]
    public async Task SetCubeTransfer_PassesPoints()
    {
        IReadOnlyList<CubeTransferPoint>? seen = null;
        var tool = new SetCubeTransferTool((points, _) => { seen = points; return Task.FromResult<CubeViewState?>(SampleState()); });
        await tool.InvokeAsync(Args("""{"points":[{"x":0,"y":0},{"x":0.4,"y":0.9},{"x":1,"y":1}]}"""), Ctx, default);
        Assert.Equal(3, seen!.Count);
        Assert.Equal(0.4, seen[1].X);
        Assert.Equal(0.9, seen[1].Y);
    }

    [Fact]
    public async Task SetCubeTransfer_Reset()
    {
        bool? seenReset = null;
        var tool = new SetCubeTransferTool((_, reset) => { seenReset = reset; return Task.FromResult<CubeViewState?>(SampleState()); });
        await tool.InvokeAsync(Args("""{"reset":true}"""), Ctx, default);
        Assert.True(seenReset);
    }

    [Fact]
    public async Task SetCubeTransfer_TooFewPoints_InvalidArgument()
    {
        var tool = new SetCubeTransferTool((_, _) => Task.FromResult<CubeViewState?>(SampleState()));
        var r = await tool.InvokeAsync(Args("""{"points":[{"x":0,"y":0}]}"""), Ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(r).Reason);
    }

    [Fact]
    public async Task SetCubeTransfer_NoViewer_TargetNotResolved()
    {
        var tool = new SetCubeTransferTool((_, _) => Task.FromResult<CubeViewState?>(null));
        var r = await tool.InvokeAsync(Args("""{"reset":true}"""), Ctx, default);
        Assert.IsType<TargetNotResolved>(Assert.IsType<FailedResult>(r).Reason);
    }

    // ── get_cube_channel_profile ──────────────────────────────────────────────

    [Fact]
    public async Task GetCubeChannelProfile_ReturnsMeansWithNullBlanks()
    {
        var tool = new GetCubeChannelProfileTool(() => Task.FromResult<CubeChannelProfileResult?>(
            new(3, new double?[] { 0.5, null, 1.5 }, "Jy/beam", new double[] { 100, 110, 120 }, "GHz")));
        var doc = Json(await tool.InvokeAsync(Args("""{}"""), Ctx, default));
        Assert.Equal(3, doc.GetProperty("channels").GetInt32());
        Assert.Equal(JsonValueKind.Null, doc.GetProperty("mean")[1].ValueKind);
        Assert.Equal(110, doc.GetProperty("spectralAxis")[1].GetDouble());
    }

    [Fact]
    public async Task GetCubeChannelProfile_NoCube_TargetNotResolved()
    {
        var tool = new GetCubeChannelProfileTool(() => Task.FromResult<CubeChannelProfileResult?>(null));
        var r = await tool.InvokeAsync(Args("""{}"""), Ctx, default);
        Assert.IsType<TargetNotResolved>(Assert.IsType<FailedResult>(r).Reason);
    }

    // ── switch_cube_tab / list_recent_cubes ───────────────────────────────────

    [Fact]
    public async Task SwitchCubeTab_PassesIndex()
    {
        int? seen = null;
        var tool = new SwitchCubeTabTool(i => { seen = i; return Task.FromResult(new CubeTabSwitchOutcome(true, i, 3, "M31", null)); });
        var doc = Json(await tool.InvokeAsync(Args("""{"index":2}"""), Ctx, default));
        Assert.Equal(2, seen);
        Assert.True(doc.GetProperty("switched").GetBoolean());
        Assert.Equal("M31", doc.GetProperty("activeName").GetString());
    }

    [Fact]
    public async Task SwitchCubeTab_MissingIndex_InvalidArgument()
    {
        var tool = new SwitchCubeTabTool(_ => Task.FromResult(new CubeTabSwitchOutcome(false, 0, 0, null, null)));
        var r = await tool.InvokeAsync(Args("""{}"""), Ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(r).Reason);
    }

    [Fact]
    public async Task ListRecentCubes_ReturnsEntries()
    {
        var tool = new ListRecentCubesTool(() => Task.FromResult<IReadOnlyList<RecentCubeInfo>>(new[]
        {
            new RecentCubeInfo("M31 cube", @"C:\data\m31.fits", DateTime.UtcNow),
        }));
        var doc = Json(await tool.InvokeAsync(Args("""{}"""), Ctx, default));
        Assert.Equal(1, doc.GetProperty("count").GetInt32());
        Assert.Equal("M31 cube", doc.GetProperty("recents")[0].GetProperty("name").GetString());
    }
}
