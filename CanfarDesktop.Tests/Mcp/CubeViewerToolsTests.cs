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

    // ── The reads that are about the whole file ─────────────────────────────────────────────────

    /// <summary>
    /// The waveform a person scrubs against to find the line. It was on screen and nowhere else, so
    /// "go to the brightest channel" meant probing a spaxel nothing said was on the source, or walking
    /// every channel. The peak is reported outright, because that is what this is usually asked for.
    /// </summary>
    [Fact]
    public async Task ChannelProfile_ReportsTheIntensityPerChannelAndThePeak()
    {
        var tool = new GetCubeChannelProfileTool(() => Task.FromResult<CubeChannelProfileResult?>(
            new CubeChannelProfileResult(
                Channels: 4,
                SpectralAxis: [115.20, 115.21, 115.22, 115.23],
                Intensity: [0.1, 0.9, 0.3, 0.2],
                SpectralUnit: "GHz",
                PeakChannel: 1,
                CurrentChannel: 0,
                SpectralFrame: "LSRK",
                RestFrequencyGHz: 115.271)));

        var doc = Json(await tool.InvokeAsync(Args("{}"), Ctx, default));

        Assert.Equal(4, doc.GetProperty("channels").GetInt32());
        Assert.Equal(1, doc.GetProperty("peakChannel").GetInt32());
        Assert.Equal(0, doc.GetProperty("currentChannel").GetInt32());
        Assert.Equal("GHz", doc.GetProperty("spectralUnit").GetString());
        Assert.Equal("LSRK", doc.GetProperty("spectralFrame").GetString());
    }

    /// <summary>No cube loaded is a named failure, not an empty profile that reads as a flat spectrum.</summary>
    [Fact]
    public async Task ChannelProfile_WithNoCube_SaysToOpenOne()
    {
        var tool = new GetCubeChannelProfileTool(() => Task.FromResult<CubeChannelProfileResult?>(null));
        var failure = Assert.IsType<FailedResult>(await tool.InvokeAsync(Args("{}"), Ctx, default));

        Assert.Contains("open_cube", failure.Reason.Description);
    }

    /// <summary>The description sends a caller wanting one spaxel to the tool that does that instead.</summary>
    [Fact]
    public void ChannelProfile_DistinguishesItselfFromProbeCubeSpectrum()
    {
        var tool = new GetCubeChannelProfileTool(() => Task.FromResult<CubeChannelProfileResult?>(null));
        Assert.Contains("probe_cube_spectrum", tool.Descriptor.Description);
    }

    /// <summary>
    /// A recents list outlives the files in it, so each entry says whether its file is still there —
    /// knowing before beats "open this" failing after the fact.
    /// </summary>
    [Fact]
    public async Task RecentCubes_CarryRealPathsAndWhetherTheFileIsStillThere()
    {
        var tool = new ListRecentCubesTool(() => Task.FromResult<IReadOnlyList<RecentCubeView>>(
        [
            new RecentCubeView(@"C:\data\m51.fits", "M51", true),
            new RecentCubeView(@"D:\gone\old.fits", "old", false),
        ]));

        var doc = Json(await tool.InvokeAsync(Args("{}"), Ctx, default));
        var cubes = doc.GetProperty("cubes");

        Assert.Equal(2, doc.GetProperty("count").GetInt32());
        Assert.Equal(@"C:\data\m51.fits", cubes[0].GetProperty("path").GetString());
        Assert.True(cubes[0].GetProperty("exists").GetBoolean());
        Assert.False(cubes[1].GetProperty("exists").GetBoolean());
    }

    [Fact]
    public async Task RecentCubes_EmptyIsAnEmptyListNotAFailure()
    {
        var tool = new ListRecentCubesTool(() => Task.FromResult<IReadOnlyList<RecentCubeView>>([]));
        var doc = Json(await tool.InvokeAsync(Args("{}"), Ctx, default));

        Assert.Equal(0, doc.GetProperty("count").GetInt32());
        Assert.Equal(0, doc.GetProperty("cubes").GetArrayLength());
    }
}
