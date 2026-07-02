using Xunit;
using CanfarDesktop.Services.CubeViewer;

namespace CanfarDesktop.Tests.Services.CubeViewer;

/// <summary>
/// The extended info-panel statistics text: RANGE (display cut) vs MIN/MAX (true extremes),
/// MEDIAN, and the Resident/Downsampled MODE line. Pure formatting over CubeMetadata.
/// </summary>
public class CubeMetadataStatsTests
{
    private static CubeMetadata Meta(
        int nx = 100, int ny = 100, int nz = 50,
        int rnx = 100, int rny = 100, int rnz = 50,
        string bunit = "Jy/beam") => new()
    {
        Nx = nx, Ny = ny, Nz = nz,
        RenderNx = rnx, RenderNy = rny, RenderNz = rnz,
        DataMin = -0.25, DataMax = 12.5, Median = 0.12345,
        NormLo = 0.001, NormHi = 5.0,
        Bunit = bunit,
    };

    [Fact]
    public void CutRangeText_IsTheNormalizationWindow_WithUnit()
    {
        Assert.Equal("0.001 … 5 Jy/beam", Meta().CutRangeText);
    }

    [Fact]
    public void MinMaxText_IsTheTrueExtremes()
    {
        Assert.Equal("-0.25 / 12.5", Meta().MinMaxText);
    }

    [Fact]
    public void MedianText_UsesTheSharedStatFormat()
    {
        Assert.Equal("0.123", Meta().MedianText);
    }

    [Fact]
    public void RangeText_Unchanged_FullExtremesWithUnit()
    {
        // The pre-existing property (export/consumers) keeps its min…max semantics.
        Assert.Equal("-0.25 … 12.5 Jy/beam", Meta().RangeText);
    }

    [Fact]
    public void CutRangeText_OmitsUnit_WhenBunitEmpty()
    {
        Assert.Equal("0.001 … 5", Meta(bunit: "").CutRangeText);
    }

    [Fact]
    public void Mode_Resident_WhenRenderDimsMatchNative()
    {
        var m = Meta();

        Assert.False(m.IsDownsampled);
        Assert.Equal("Resident (full)", m.ModeText);
    }

    [Theory]
    [InlineData(50, 100, 50)]  // X strided
    [InlineData(100, 50, 50)]  // Y strided
    [InlineData(100, 100, 25)] // Z strided
    public void Mode_Downsampled_WhenAnyAxisWasStrided(int rnx, int rny, int rnz)
    {
        var m = Meta(rnx: rnx, rny: rny, rnz: rnz);

        Assert.True(m.IsDownsampled);
        Assert.Equal("Downsampled to GPU cap", m.ModeText);
    }
}
