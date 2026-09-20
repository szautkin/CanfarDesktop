using Xunit;
using CanfarDesktop.Models;
using CanfarDesktop.Models.Fits;
using CanfarDesktop.Views.FitsViewer;

namespace CanfarDesktop.Tests.Views;

/// <summary>
/// The FITS canvas as the annotation renderer sees it. This is the arithmetic that decides where a mark
/// ends up, and "it looked right" is not a way to check it — so the surface takes functions rather than
/// a page, and they are supplied here.
/// </summary>
public class FitsAnnotationSurfaceTests
{
    /// <summary>A tangent-plane WCS at 1 arcsec/pixel, north up, centred on M31.</summary>
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

    /// <summary>The image the WCS above describes: 200x200, so the flip has something to flip about.</summary>
    private const int ImageHeight = 200;
    private const int ImageWidth = 200;

    /// <summary>A canvas at 2x zoom with the image origin at (50, 50) on screen.</summary>
    private static FitsAnnotationSurface Zoomed(WcsInfo? wcs = null)
        => new((x, y) => (50 + x * 2, 50 + y * 2), () => wcs, () => (ImageWidth, ImageHeight));

    [Fact]
    public void AnImagePixelGoesThroughTheCanvasTransform()
    {
        var at = Zoomed().Project(AnnotationAnchor.ImagePixel(10, 20));

        Assert.Equal(70, at!.Value.X, 6);
        Assert.Equal(90, at.Value.Y, 6);
    }

    /// <summary>
    /// A sky position lands where WorldToPixel says — after the two pixel conventions are reconciled.
    /// WorldToPixel answers 1-based FITS pixels counting up from the bottom; the canvas counts 0-based
    /// display pixels down from the top.
    /// </summary>
    [Fact]
    public void ASkyPositionGoesThroughTheWcsAndThenTheTransform()
    {
        var wcs = Wcs();
        var at = Zoomed(wcs).Project(AnnotationAnchor.Sky(wcs.CrVal1, wcs.CrVal2));

        var fits = wcs.WorldToPixel(wcs.CrVal1, wcs.CrVal2)!.Value;
        var displayX = fits.Px - 1;
        var displayY = ImageHeight - 1 - (fits.Py - 1);

        Assert.Equal(50 + displayX * 2, at!.Value.X, 3);
        Assert.Equal(50 + displayY * 2, at.Value.Y, 3);
    }

    /// <summary>
    /// The two directions agree. A press becomes a sky anchor and the anchor becomes a point on the
    /// canvas, and the point has to be where the press was — mixing the conventions leaves a mark
    /// mirrored and one pixel out, which reads as a rendering wobble rather than a coordinate bug.
    /// </summary>
    [Theory]
    [InlineData(100.0, 100.0)]
    [InlineData(10.0, 190.0)]
    [InlineData(175.5, 42.25)]
    public void APressAndItsMarkLandInTheSamePlace(double displayX, double displayY)
    {
        var wcs = Wcs();
        var anchor = FitsAnnotationSurface.SkyAt(wcs, ImageHeight, displayX, displayY);
        Assert.NotNull(anchor);

        var back = Zoomed(wcs).Project(anchor!);
        Assert.NotNull(back);

        // The canvas transform is (50 + x*2, 50 + y*2), so the press at displayX/Y is here:
        Assert.Equal(50 + displayX * 2, back!.Value.X, 3);
        Assert.Equal(50 + displayY * 2, back.Value.Y, 3);
    }

    [Fact]
    public void WithoutAWcsAPressHasNoSkyPosition()
        => Assert.Null(FitsAnnotationSurface.SkyAt(null, ImageHeight, 100, 100));

    /// <summary>Without WCS a sky mark has nowhere to go — and is skipped rather than drawn somewhere.</summary>
    [Fact]
    public void ASkyMarkOnAnImageWithNoWcsIsNotPlaced()
        => Assert.Null(Zoomed(wcs: null).Project(AnnotationAnchor.Sky(10, 41)));

    /// <summary>
    /// A cube mark on a FITS canvas belongs to another viewer's space. Not an error, and not clamped: a
    /// clamped mark points at the wrong thing.
    /// </summary>
    [Fact]
    public void ACubeMarkIsNotPlacedOnAFitsCanvas()
        => Assert.Null(Zoomed(Wcs()).Project(AnnotationAnchor.Data(1, 2, 3)));

    [Fact]
    public void AnImpossibleAnchorIsNotPlaced()
        => Assert.Null(Zoomed(Wcs()).Project(AnnotationAnchor.ImagePixel(double.NaN, 0)));

    // ── Bounds ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The bug, in one test. A mosaic's extensions each carry their own WCS, so a mark pinned on one CCD
    /// projects through the next one's to a perfectly finite pixel far off the frame. The canvas has no
    /// edge to stop it, so it was drawn anyway — parked over the image, pointing at nothing.
    ///
    /// The position below is real arithmetic, not a guess: it is 500 pixels of Dec north of the top of
    /// the frame, which the WCS maps to a negative display row.
    /// </summary>
    [Fact]
    public void AMarkFromAnotherExtensionIsNotDrawnOnThisOne()
    {
        var wcs = Wcs();

        // 700 pixels north of the reference pixel, on a 200-row image: off the top by 500.
        var far = AnnotationAnchor.Sky(wcs.CrVal1, wcs.CrVal2 + 700.0 / 3600);

        var display = PixelConvention.DisplayOfSky(wcs, ImageHeight, far.X, far.Y);
        Assert.NotNull(display);
        Assert.True(display!.Value.Y < 0, $"the fixture must put it off the frame, got {display.Value.Y}");

        Assert.Null(Zoomed(wcs).Project(far));
    }

    /// <summary>
    /// Off the frame in any direction, and in either anchor space. A pixel anchor is just as capable of
    /// naming a row this image does not have — that is what a mark carried over from a bigger extension
    /// looks like.
    /// </summary>
    [Theory]
    [InlineData(-1.0, 100.0)]
    [InlineData(100.0, -1.0)]
    [InlineData(ImageWidth + 1.0, 100.0)]
    [InlineData(100.0, ImageHeight + 1.0)]
    [InlineData(-4000.0, -4000.0)]
    public void APixelOutsideTheFrameIsNotPlaced(double x, double y)
        => Assert.Null(Zoomed().Project(AnnotationAnchor.ImagePixel(x, y)));

    /// <summary>
    /// The edge belongs to the image. Inclusive on all four sides, because rejecting the border would
    /// make the one place a mark is hardest to notice the one place it silently disappears.
    /// </summary>
    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.0, ImageHeight)]
    [InlineData(ImageWidth, 0.0)]
    [InlineData(ImageWidth, ImageHeight)]
    [InlineData(ImageWidth - 1.0, ImageHeight - 1.0)]
    public void AMarkOnTheEdgeIsStillOnTheImage(double x, double y)
        => Assert.NotNull(Zoomed().Project(AnnotationAnchor.ImagePixel(x, y)));

    /// <summary>With no image loaded there is no frame, so there is nowhere for a mark to be.</summary>
    [Fact]
    public void WithNoImageLoadedNothingIsPlaced()
    {
        var empty = new FitsAnnotationSurface((x, y) => (x, y), () => null, () => (0, 0));

        Assert.Null(empty.Project(AnnotationAnchor.ImagePixel(10, 10)));
    }

    /// <summary>
    /// The regression the bounds test could easily have caused. The scale is MEASURED by projecting a
    /// point one unit away, and for a mark on the edge of the frame that point is off it — so running
    /// the measurement through the clipped projection would return the 1.0 fallback and shrink the mark
    /// to a dot at exactly the moment it reached the border.
    /// </summary>
    [Fact]
    public void AMarkOnTheEdgeIsStillMeasuredAtFullScale()
    {
        var atEdge = Zoomed().UnitsToPixels(AnnotationAnchor.ImagePixel(ImageWidth, 100));

        Assert.Equal(2.0, atEdge, 6);
    }

    // ── Scale ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Measured rather than derived: project the anchor and a point one unit away. That stays correct
    /// through zoom, rotation, a parity flip and the image's display scaling, without this code knowing
    /// any of them exist.
    /// </summary>
    [Fact]
    public void OneImagePixelIsTheZoomFactorInScreenPixels()
        => Assert.Equal(2.0, Zoomed().UnitsToPixels(AnnotationAnchor.ImagePixel(10, 10)), 6);

    [Fact]
    public void AScaleSurvivesRotation()
    {
        // A 90-degree rotation about the origin: x and y swap. The SPAN of one pixel is unchanged, which
        // is the whole point of measuring it instead of reading the transform's x-scale.
        var rotated = new FitsAnnotationSurface((x, y) => (-y * 3, x * 3), () => null, () => (ImageWidth, ImageHeight));

        Assert.Equal(3.0, rotated.UnitsToPixels(AnnotationAnchor.ImagePixel(10, 10)), 6);
    }

    /// <summary>
    /// A degree of RA is not a degree on the sky except at the equator, so the step is taken in Dec —
    /// where one degree is one degree everywhere, and a circle drawn with it is the size it says it is.
    /// </summary>
    [Fact]
    public void OneDegreeOfSkyIsTheSameSpanWhateverTheDeclination()
    {
        var wcs = Wcs();
        var surface = Zoomed(wcs);

        // 1 arcsec per pixel at 2x zoom → 7200 screen pixels per degree.
        var atCentre = surface.UnitsToPixels(AnnotationAnchor.Sky(wcs.CrVal1, wcs.CrVal2));
        Assert.Equal(7200, atCentre, 0);

        // A position at a very different RA on the same field must not report a different scale just
        // because RA degrees are shorter there.
        var offset = surface.UnitsToPixels(AnnotationAnchor.Sky(wcs.CrVal1 + 0.01, wcs.CrVal2));
        Assert.Equal(atCentre, offset, 0);
    }

    /// <summary>
    /// A degenerate scale draws a mark with no size, which looks like a mark that was lost. One is the
    /// honest fallback: the mark is drawn at its stated size in screen pixels rather than vanishing.
    /// </summary>
    [Fact]
    public void ACollapsedTransformDoesNotProduceASizelessMark()
    {
        var collapsed = new FitsAnnotationSurface((_, _) => (0, 0), () => null, () => (ImageWidth, ImageHeight));

        Assert.Equal(1.0, collapsed.UnitsToPixels(AnnotationAnchor.ImagePixel(10, 10)));
    }

    [Fact]
    public void TheScreenIsTheDefaultInkScale()
    {
        Assert.Equal(1.0, Zoomed().InkScale);
        Assert.Equal(4.0, new FitsAnnotationSurface((x, y) => (x, y), () => null, () => (ImageWidth, ImageHeight)) { InkScale = 4.0 }.InkScale);
    }
}
