using Xunit;
using CanfarDesktop.Services.CubeViewer;

namespace CanfarDesktop.Tests.Services.CubeViewer;

/// <summary>
/// The channel-scrubber waveform math: NaN-aware per-channel means over the down-sampled volume
/// and the [0,1] height normalization the canvas draws. Pure + deterministic.
/// </summary>
public class CubeChannelProfileTests
{
    private static Half[] Cube(params float[] values)
    {
        var data = new Half[values.Length];
        for (int i = 0; i < values.Length; i++) data[i] = (Half)values[i];
        return data;
    }

    // ── Compute (per-channel means) ──

    [Fact]
    public void Compute_MeansEachPlane()
    {
        // 2×1×3: plane means are 1, 3, 5.
        var prof = CubeChannelProfile.Compute(Cube(0, 2, 2, 4, 4, 6), 2, 1, 3);

        Assert.NotNull(prof);
        Assert.Equal(new[] { 1f, 3f, 5f }, prof!);
    }

    [Fact]
    public void Compute_IgnoresNaN_InPlaneMean()
    {
        // Plane 0: (NaN, 4) → mean of the finite voxel = 4. Plane 1: all finite.
        var prof = CubeChannelProfile.Compute(Cube(float.NaN, 4, 1, 3), 2, 1, 2);

        Assert.NotNull(prof);
        Assert.Equal(4f, prof![0]);
        Assert.Equal(2f, prof[1]);
    }

    [Fact]
    public void Compute_AllNaNChannel_YieldsNaN()
    {
        var prof = CubeChannelProfile.Compute(Cube(float.NaN, float.NaN, 1, 1), 2, 1, 2);

        Assert.NotNull(prof);
        Assert.True(float.IsNaN(prof![0]));
        Assert.Equal(1f, prof[1]);
    }

    [Fact]
    public void Compute_SingleChannel_ReturnsNull()
    {
        // nz < 2 → nothing to scrub, no waveform.
        Assert.Null(CubeChannelProfile.Compute(Cube(1, 2), 2, 1, 1));
    }

    [Fact]
    public void Compute_TruncatedData_ReturnsNull()
    {
        // Data shorter than nx·ny·nz must not be indexed out of range.
        Assert.Null(CubeChannelProfile.Compute(Cube(1, 2), 2, 2, 2));
    }

    // ── NormalizedHeights (waveform mapping) ──

    [Fact]
    public void NormalizedHeights_MapsMinToZero_MaxToOne()
    {
        var h = CubeChannelProfile.NormalizedHeights(new[] { 1f, 3f, 2f });

        Assert.Equal(0f, h[0]);
        Assert.Equal(1f, h[1]);
        Assert.Equal(0.5f, h[2]);
    }

    [Fact]
    public void NormalizedHeights_NonFinite_MapToBaseline()
    {
        var h = CubeChannelProfile.NormalizedHeights(new[] { float.NaN, 1f, 2f, float.PositiveInfinity });

        Assert.Equal(0f, h[0]);
        Assert.Equal(0f, h[3]);
        Assert.Equal(1f, h[2]);
    }

    [Fact]
    public void NormalizedHeights_FlatProfile_IsBaseline()
    {
        // Constant profile: range falls back to 1 so everything maps to 0 (macOS behavior).
        Assert.All(CubeChannelProfile.NormalizedHeights(new[] { 2f, 2f, 2f }), v => Assert.Equal(0f, v));
    }

    [Fact]
    public void NormalizedHeights_AllNaN_IsBaseline()
    {
        Assert.All(CubeChannelProfile.NormalizedHeights(new[] { float.NaN, float.NaN }), v => Assert.Equal(0f, v));
    }
}
