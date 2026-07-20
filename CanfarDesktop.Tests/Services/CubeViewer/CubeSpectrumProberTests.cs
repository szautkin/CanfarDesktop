using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Services.CubeViewer;

namespace CanfarDesktop.Tests.Services.CubeViewer;

/// <summary>
/// Regression tests for QA finding F10: probing a real (masked, GPU-downsampled) cube crashed the
/// JSON serializer on NaN/Inf and rejected valid native pixels as out of range.
/// </summary>
public class CubeSpectrumProberTests
{
    // A native 8×6×5 cube down-sampled with stride 2 → 4×3×3 volume, physical range 0…10.
    // Voxel (vx=1, vy=1) carries [0.5, NaN, 0.25] over the three rendered channels.
    private static VolumeData MaskedVolume()
    {
        int nx = 4, ny = 3, nz = 3;
        var data = new Half[nx * ny * nz];
        Array.Fill(data, (Half)0.1f);
        data[(0 * ny + 1) * nx + 1] = (Half)0.5f;
        data[(1 * ny + 1) * nx + 1] = Half.NaN;      // masked cell (Faraday-cube class from QA)
        data[(2 * ny + 1) * nx + 1] = (Half)0.25f;

        var meta = new CubeMetadata
        {
            Nx = 8, Ny = 6, Nz = 5,
            RenderNx = nx, RenderNy = ny, RenderNz = nz,
            Stride = 2,
            NormLo = 0, NormHi = 10,
            Bunit = "Jy/beam",
            Wcs = new CubeWcs { Nx = 8, Ny = 6, Nz = 5, SpecCType = "FREQ", SpecCrpix = 1, SpecCrval = 100, SpecCdelt = 5 },
        };
        return new VolumeData(nx, ny, nz, data, "test-cube", meta);
    }

    [Fact]
    public void NoCube_IsTyped()
        => Assert.Equal(CubeProbeStatus.NoCube, CubeSpectrumProber.Probe(null, 0, 0).Status);

    [Fact]
    public void NativeCoords_InsideCube_ButBeyondDownsampledVolume_AreAccepted()
    {
        // The F10 repro: on a downsampled cube every native pixel past the render dims read as
        // out-of-range. Native (7, 5) is the far corner of the 8×6 cube — it must probe fine.
        var probe = CubeSpectrumProber.Probe(MaskedVolume(), 7, 5);
        Assert.Equal(CubeProbeStatus.Ok, probe.Status);
    }

    [Fact]
    public void OutOfRange_ReportsNativeDims()
    {
        var probe = CubeSpectrumProber.Probe(MaskedVolume(), 8, 5);
        Assert.Equal(CubeProbeStatus.OutOfRange, probe.Status);
        Assert.Equal(8, probe.Nx);
        Assert.Equal(6, probe.Ny);
    }

    [Fact]
    public void MaskedVoxel_BecomesNullFlux_AndCounts()
    {
        var probe = CubeSpectrumProber.Probe(MaskedVolume(), 2, 3); // native (2,3) → volume (1,1)
        Assert.Equal(CubeProbeStatus.Ok, probe.Status);
        var r = probe.Result!;
        Assert.Equal(new double?[] { 5.0, null, 2.5 }, r.Flux);
        Assert.Equal(1, r.BlankedChannels);
    }

    [Fact]
    public void SpectralAxis_UsesNativeChannels_NotRenderIndices()
    {
        // Stride 2: rendered channels 0,1,2 are native 0,2,4 → SpectralValue 100,110,120 (not 100,105,110).
        var r = CubeSpectrumProber.Probe(MaskedVolume(), 2, 3).Result!;
        Assert.Equal(new double[] { 100, 110, 120 }, r.SpectralAxis);
    }

    [Fact]
    public void Result_WithMaskedVoxels_SerializesToValidJson()
    {
        // F10a: the exact QA crash — serializing a masked spaxel through the MCP options must not throw.
        var r = CubeSpectrumProber.Probe(MaskedVolume(), 2, 3).Result!;
        var json = JsonSerializer.SerializeToUtf8Bytes(r, McpJson.Options);
        var doc = JsonDocument.Parse(json).RootElement;
        Assert.Equal(JsonValueKind.Null, doc.GetProperty("flux")[1].ValueKind);
    }

    [Fact]
    public void FullResolutionCube_ProbesIdentityMapping()
    {
        int nx = 3, ny = 2, nz = 2;
        var data = new Half[nx * ny * nz];
        Array.Fill(data, (Half)0.5f);
        var vol = new VolumeData(nx, ny, nz, data, "small"); // no meta → volume dims are native dims
        var probe = CubeSpectrumProber.Probe(vol, 2, 1);
        Assert.Equal(CubeProbeStatus.Ok, probe.Status);
        Assert.Equal(2, probe.Result!.Flux.Length);
        Assert.Equal(CubeProbeStatus.OutOfRange, CubeSpectrumProber.Probe(vol, 3, 0).Status);
    }
}
