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

    /// <summary>A canvas at 2x zoom with the image origin at (50, 50) on screen.</summary>
    private static FitsAnnotationSurface Zoomed(WcsInfo? wcs = null)
        => new((x, y) => (50 + x * 2, 50 + y * 2), () => wcs);

    [Fact]
    public void AnImagePixelGoesThroughTheCanvasTransform()
    {
        var at = Zoomed().Project(AnnotationAnchor.ImagePixel(10, 20));

        Assert.Equal(70, at!.Value.X, 6);
        Assert.Equal(90, at.Value.Y, 6);
    }

    [Fact]
    public void ASkyPositionGoesThroughTheWcsAndThenTheTransform()
    {
        var wcs = Wcs();
        var at = Zoomed(wcs).Project(AnnotationAnchor.Sky(wcs.CrVal1, wcs.CrVal2));

        // The reference point is at CRPIX (1-based FITS convention aside, the page's own transform is
        // what is being exercised here — the point is that it lands where WorldToPixel says).
        var expected = wcs.WorldToPixel(wcs.CrVal1, wcs.CrVal2)!.Value;
        Assert.Equal(50 + expected.Px * 2, at!.Value.X, 3);
        Assert.Equal(50 + expected.Py * 2, at.Value.Y, 3);
    }

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
        var rotated = new FitsAnnotationSurface((x, y) => (-y * 3, x * 3), () => null);

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
        var collapsed = new FitsAnnotationSurface((_, _) => (0, 0), () => null);

        Assert.Equal(1.0, collapsed.UnitsToPixels(AnnotationAnchor.ImagePixel(10, 10)));
    }

    [Fact]
    public void TheScreenIsTheDefaultInkScale()
    {
        Assert.Equal(1.0, Zoomed().InkScale);
        Assert.Equal(4.0, new FitsAnnotationSurface((x, y) => (x, y), () => null) { InkScale = 4.0 }.InkScale);
    }
}
