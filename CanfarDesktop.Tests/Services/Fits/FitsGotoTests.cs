using Xunit;
using CanfarDesktop.Models.Fits;
using CanfarDesktop.Services.Fits;

namespace CanfarDesktop.Tests.Services.Fits;

/// <summary>
/// Where a sky position lands on the open image — the one answer the viewer's Go To and
/// fits_goto_coordinate share.
///
/// <para>They answered separately. The viewer refused a position off the image with a status
/// message, while the tool only asked whether the WCS could place it at all and reported
/// <c>moved: true</c> — for M31's nucleus on a 40″ HST frame five arcminutes away, with the
/// crosshair left where it was.</para>
/// </summary>
public class FitsGotoTests
{
    private const int Width = 1027, Height = 1024;

    /// <summary>A plain TAN WCS for a 1027 × 1024, 0.04″ frame near M31 — distortion is not the point here.</summary>
    private static WcsInfo Wcs() => new()
    {
        CType1 = "RA---TAN", CType2 = "DEC--TAN",
        CrPix1 = 514, CrPix2 = 512,
        CrVal1 = 10.5777, CrVal2 = 41.2336,
        Cd1_1 = -1.1e-5, Cd2_2 = 1.1e-5,
    };

    [Fact]
    public void APositionOnTheImageIsWhereTheViewerReadsIt()
    {
        var wcs = Wcs();
        var (ra, dec) = PixelConvention.SkyAtDisplay(wcs, Height, 300, 700);

        var target = FitsGoto.Resolve(wcs, Width, Height, ra, dec);

        Assert.True(target.OnImage);
        Assert.Equal(300, target.X, 6);
        Assert.Equal(700, target.Y, 6);
        Assert.Null(target.Why);
    }

    /// <summary>The report's case: M31's nucleus, about five arcminutes off this frame.</summary>
    [Fact]
    public void APositionOffTheImageIsNotAMove()
    {
        var target = FitsGoto.Resolve(Wcs(), Width, Height, 10.684708, 41.26875);

        Assert.False(target.OnImage);
        Assert.Contains("outside the image", target.Why);
    }

    [Theory]
    [InlineData(-0.5, 10)]
    [InlineData(Width, 10)]
    [InlineData(10, -0.5)]
    [InlineData(10, Height)]
    public void JustPastAnEdgeIsOff(double x, double y)
    {
        var wcs = Wcs();
        var (ra, dec) = PixelConvention.SkyAtDisplay(wcs, Height, x, y);

        Assert.False(FitsGoto.Resolve(wcs, Width, Height, ra, dec).OnImage);
    }

    [Fact]
    public void WithoutAWcsThereIsNowhereToGo()
    {
        var target = FitsGoto.Resolve(null, Width, Height, 10.5, 41.2);

        Assert.False(target.OnImage);
        Assert.Contains("WCS", target.Why);
    }

    [Fact]
    public void APositionTheProjectionCannotReachIsNotAMove()
    {
        // The antipode of a TAN reference point is outside the projection's hemisphere.
        var target = FitsGoto.Resolve(Wcs(), Width, Height, 10.5777 + 180, -41.2336);

        Assert.False(target.OnImage);
        Assert.NotNull(target.Why);
    }
}
