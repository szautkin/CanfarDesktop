using Xunit;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Models.Fits;
using CanfarDesktop.Views.FitsViewer;

namespace CanfarDesktop.Tests.Views;

/// <summary>
/// The exported figure as the annotation renderer sees it, and the regions a figure can be OF.
/// </summary>
public class FitsExportSurfaceTests
{
    private const int ImageWidth = 200, ImageHeight = 200;

    /// <summary>A tangent-plane WCS at 1 arcsec/pixel, north up, centred in a 200x200 image.</summary>
    private static WcsInfo Wcs() => new()
    {
        CrPix1 = 100,
        CrPix2 = 100,
        CrVal1 = 10.6847,
        CrVal2 = 41.2687,
        Cd1_1 = -1.0 / 3600,
        Cd1_2 = 0,
        Cd2_1 = 0,
        Cd2_2 = 1.0 / 3600,
        CType1 = "RA---TAN",
        CType2 = "DEC--TAN",
    };

    /// <summary>A 100x100 region of the image, rendered onto a 400x400 plate — four plate pixels per image pixel.</summary>
    private static FitsExportSurface Plate(double inkScale = 4.0, WcsInfo? wcs = null)
        => new(new FitsRegion(50, 50, 100, 100), 400, 400, wcs, ImageHeight) { InkScale = inkScale };

    // ── The surface ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheRegionsCornerIsThePlatesCorner()
    {
        var at = Plate().Project(AnnotationAnchor.ImagePixel(50, 50));

        Assert.Equal(0, at!.Value.X, 6);
        Assert.Equal(0, at.Value.Y, 6);
    }

    [Fact]
    public void AnImagePixelIsScaledIntoThePlate()
    {
        var at = Plate().Project(AnnotationAnchor.ImagePixel(100, 75));

        Assert.Equal((100 - 50) * 4, at!.Value.X, 6);
        Assert.Equal((75 - 50) * 4, at.Value.Y, 6);
    }

    /// <summary>
    /// A mark outside the region is not on this plate. Clamping it to the border would put it on the
    /// frame edge claiming to point at something inside the picture.
    /// </summary>
    [Theory]
    [InlineData(10, 100)]    // left of the region
    [InlineData(100, 190)]   // below it
    [InlineData(160, 100)]   // right of it
    public void AMarkOutsideTheRegionIsNotOnThePlate(double x, double y)
        => Assert.Null(Plate().Project(AnnotationAnchor.ImagePixel(x, y)));

    [Fact]
    public void ACubeMarkIsNotOnAFitsPlate()
        => Assert.Null(Plate().Project(AnnotationAnchor.Data(1, 2, 3)));

    /// <summary>
    /// The ink scale is the point of the exercise: a 2px ring at 4x on a plate whose title and colorbar
    /// have quadrupled is the only thing in the figure that shrank.
    /// </summary>
    [Fact]
    public void ThePlateReportsItsOwnScaleAsInk()
    {
        Assert.Equal(4.0, Plate(inkScale: 4.0).InkScale);
        Assert.Equal(1.0, Plate(inkScale: 1.0).InkScale);
    }

    [Fact]
    public void OneImagePixelIsTheFramesScale()
        => Assert.Equal(4.0, Plate().UnitsToPixels(AnnotationAnchor.ImagePixel(100, 100)), 6);

    [Fact]
    public void ASkyMarkLandsWhereItsPixelWould()
    {
        var wcs = Wcs();
        var plate = Plate(wcs: wcs);

        // The WCS reference point is at FITS (100,100) → display (99, 100) in a 200-row image.
        var sky = plate.Project(AnnotationAnchor.Sky(wcs.CrVal1, wcs.CrVal2))!.Value;
        var pixel = plate.Project(AnnotationAnchor.ImagePixel(99, 100))!.Value;

        Assert.Equal(pixel.X, sky.X, 3);
        Assert.Equal(pixel.Y, sky.Y, 3);
    }

    [Fact]
    public void ASkySizeIsConvertedThroughTheWcsAndThenTheFrame()
    {
        // 1 arcsec per image pixel, 4 plate pixels per image pixel → 3600*4 plate pixels per degree.
        var scale = Plate(wcs: Wcs()).UnitsToPixels(AnnotationAnchor.Sky(10.6847, 41.2687));

        Assert.Equal(3600 * 4, scale, 0);
    }

    [Fact]
    public void WithoutAWcsASkyMarkIsNotOnThePlate()
        => Assert.Null(Plate(wcs: null).Project(AnnotationAnchor.Sky(10.6847, 41.2687)));

    [Fact]
    public void ACollapsedFrameDoesNotProduceSizelessMarks()
    {
        var collapsed = new FitsExportSurface(new FitsRegion(0, 0, 100, 100), 0, 0, null, ImageHeight);

        Assert.Null(collapsed.Project(AnnotationAnchor.ImagePixel(10, 10)));
        Assert.Equal(1.0, collapsed.UnitsToPixels(AnnotationAnchor.ImagePixel(10, 10)));
    }

    // ── Regions ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>A drag can go up and to the left, and it means the same rectangle.</summary>
    [Fact]
    public void ARegionFromTwoCornersIsTheSameWhicheverWayItWasDragged()
    {
        var forward = FitsRegion.FromCorners(10, 20, 110, 220);
        var backward = FitsRegion.FromCorners(110, 220, 10, 20);

        Assert.Equal(forward, backward);
        Assert.Equal(100, forward.Width);
        Assert.Equal(200, forward.Height);
    }

    [Fact]
    public void ARegionWithNoAreaIsNotOne()
    {
        Assert.False(new FitsRegion(0, 0, 0, 10).IsValid);
        Assert.False(new FitsRegion(0, 0, 10, double.NaN).IsValid);
        Assert.True(new FitsRegion(0, 0, 10, 10).IsValid);
    }

    [Fact]
    public void ARegionIsTrimmedToTheImage()
    {
        var trimmed = new FitsRegion(-50, -50, 100, 100).ClampTo(ImageWidth, ImageHeight);

        Assert.Equal(new FitsRegion(0, 0, 50, 50), trimmed);
    }

    /// <summary>
    /// A region that misses the image is an answer — "that is not on this image" — rather than something
    /// to clamp into a one-pixel sliver at the edge and export.
    /// </summary>
    [Fact]
    public void ARegionOffTheImageIsRefusedRatherThanShrunkToASliver()
    {
        Assert.Null(new FitsRegion(500, 500, 100, 100).ClampTo(ImageWidth, ImageHeight));
        Assert.Null(new FitsRegion(-100, 0, 100, 100).ClampTo(ImageWidth, ImageHeight));
    }

    [Fact]
    public void PaddingGrowsARegionAroundItsSubject()
    {
        var padded = new FitsRegion(100, 100, 20, 20).Padded(0.5);

        Assert.Equal(90, padded.X);
        Assert.Equal(40, padded.Width);
        Assert.Equal(110, padded.CentreX);   // still centred on the same place
    }

    /// <summary>
    /// A sky circle's radius is MEASURED through the WCS rather than divided by a nominal pixel scale,
    /// which would be near enough on a small field and wrong on a large one.
    /// </summary>
    [Fact]
    public void ASkyCircleBecomesTheBoxThatContainsIt()
    {
        var wcs = Wcs();
        // 10 arcsec at 1 arcsec/pixel → 10 pixels, so a 20-pixel box.
        var region = FitsRegion.FromSkyCircle(wcs, ImageHeight, wcs.CrVal1, wcs.CrVal2, 10 / 3600.0);

        Assert.NotNull(region);
        Assert.Equal(20, region!.Value.Width, 1);
        Assert.Equal(20, region.Value.Height, 1);
        Assert.Equal(99, region.Value.CentreX, 1);
        Assert.Equal(100, region.Value.CentreY, 1);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void ASkyCircleWithNoRadiusIsRefused(double radius)
        => Assert.Null(FitsRegion.FromSkyCircle(Wcs(), ImageHeight, 10.6847, 41.2687, radius));

    [Fact]
    public void ASkyCircleNeedsAWcs()
        => Assert.Null(FitsRegion.FromSkyCircle(null, ImageHeight, 10.6847, 41.2687, 0.01));
}
