using Xunit;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// How big a mark is: what a drag asks for, and what gets drawn.
///
/// <para>A mark's size is stored in the anchor's own units so that it tracks the image — zoom in and a
/// circle keeps enclosing the same source. That is the right model, and it makes every SIZE LIMIT a
/// trap, because a limit written in those units means a different thing in each space.</para>
///
/// <para>It cost a real bug. The resize floor was <c>Math.Max(..., 0.5)</c> in the anchor's units:
/// half a voxel to a cube, and half a DEGREE to a sky-anchored mark. On the 0.187 arcsec/pixel image
/// this was reported from, every box came out at exactly 0.5 degrees — 19,000 pixels across, four
/// times wider than the whole frame — however small the drag. The app's own annotation list showed
/// three marks at halfWidth 0.5 and one at 0.522, which is the single drag that escaped the floor.</para>
///
/// <para>So the rule these tests hold down: limits live in SCREEN pixels, sizes live in data units,
/// and exactly one function crosses between them.</para>
/// </summary>
public class MarkSizingTests
{
    /// <summary>
    /// A surface whose scale is given directly: <paramref name="pixelsPerUnit"/> screen pixels per unit
    /// of the anchor's space. That single number is the whole difference between the spaces, which is
    /// why the bug was invisible until someone used a sky anchor.
    /// </summary>
    private sealed class Scaled : IAnnotationSurface
    {
        private readonly double _pixelsPerUnit;
        public Scaled(double pixelsPerUnit) => _pixelsPerUnit = pixelsPerUnit;

        public double InkScale { get; init; } = 1.0;

        // The anchor projects to the origin, so a screen offset IS the offset from the centre.
        public (double X, double Y)? Project(AnnotationAnchor anchor) => anchor.IsValid ? (0, 0) : null;
        public double UnitsToPixels(AnnotationAnchor anchor) => _pixelsPerUnit;
    }

    /// <summary>
    /// Screen pixels per DEGREE on the image this was reported from: 0.186984 arcsec per pixel, at the
    /// 10.7% zoom the viewer was at. Deliberately a real number rather than a round one.
    /// </summary>
    private const double SkyPixelsPerDegree = 3600.0 / 0.186984 * 0.107;

    private static Annotation Box(AnnotationAnchor anchor, double half) => new()
    {
        Id = "m1",
        Kind = AnnotationKind.Rect,
        Anchor = anchor,
        Extent = Extent.Square(half),
        Author = MarkAuthor.User,
    };

    private static AnnotationAnchor Sky() => AnnotationAnchor.Sky(241.0, 48.3);
    private static AnnotationAnchor Pixel() => AnnotationAnchor.ImagePixel(100, 100);

    // ── The regression ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The bug, in the units it happened in. A 20-pixel drag on a sky-anchored mark must ask for 20
    /// pixels' worth of sky — not half a degree.
    /// </summary>
    [Fact]
    public void ASmallDragOnASkyMarkDoesNotProduceHalfADegree()
    {
        var surface = new Scaled(SkyPixelsPerDegree);
        var mark = Box(Sky(), 0.001);

        var half = AnnotationGeometry.ResizeHalf(mark, surface, 20, 20);

        Assert.NotNull(half);
        Assert.Equal(20.0 / SkyPixelsPerDegree, half!.Value, 9);

        // The shape of the old failure: 0.5 degrees, whatever was dragged.
        Assert.True(half.Value < 0.01, $"a 20px drag asked for {half.Value} degrees");
    }

    /// <summary>
    /// The same drag in every space asks for the same number of SCREEN pixels. This is the property
    /// the old floor broke: it made the answer depend on what the units happened to mean.
    /// </summary>
    [Theory]
    [InlineData(1.0)]                    // one pixel per unit — a cube's voxels
    [InlineData(19253.0)]                // pixels per degree — a sky anchor at 1x
    [InlineData(0.08)]                   // zoomed far out
    public void ADragOfTwentyPixelsIsTwentyPixelsInEverySpace(double pixelsPerUnit)
    {
        var surface = new Scaled(pixelsPerUnit);
        var mark = Box(Pixel(), 1);

        var half = AnnotationGeometry.ResizeHalf(mark, surface, 20, 0);

        Assert.NotNull(half);
        Assert.Equal(20.0, half!.Value * pixelsPerUnit, 6);
    }

    /// <summary>
    /// The drag that creates a mark and the drag that resizes it are the same gesture, so they must
    /// agree. They now share one conversion; they used to floor on opposite sides of it.
    /// </summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(19253.0)]
    public void CreatingAndResizingAgreeAboutWhatADragMeans(double pixelsPerUnit)
    {
        var surface = new Scaled(pixelsPerUnit);
        var anchor = Sky();

        const double drag = 37.5;
        var created = AnnotationGeometry.HalfFromDrag(surface, anchor, drag);
        var resized = AnnotationGeometry.ResizeHalf(Box(anchor, created), surface, drag, 0);

        Assert.Equal(created, resized!.Value, 12);
    }

    // ── The floor ───────────────────────────────────────────────────────────────────────────────

    /// <summary>A drag of nothing still leaves something to see and something to grab.</summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(19253.0)]
    public void AZeroDragStillLeavesAGrabbableMark(double pixelsPerUnit)
    {
        var surface = new Scaled(pixelsPerUnit);

        var half = AnnotationGeometry.ResizeHalf(Box(Sky(), 0.001), surface, 0, 0);

        Assert.NotNull(half);
        Assert.Equal(AnnotationGeometry.MinimumHalfPixels, half!.Value * pixelsPerUnit, 6);
    }

    /// <summary>The floor is a floor, not a size: anything bigger is left alone.</summary>
    [Fact]
    public void ADragLargerThanTheFloorIsNotClamped()
    {
        var surface = new Scaled(1.0);

        var half = AnnotationGeometry.ResizeHalf(Box(Pixel(), 1), surface, 250, 0);

        Assert.Equal(250.0, half!.Value, 6);
    }

    /// <summary>
    /// A mark is born bigger than the floor. One placed with a click rather than a drag should look
    /// like a shape someone meant to make.
    /// </summary>
    [Fact]
    public void AMarkIsBornLargerThanTheSmallestItMayBecome()
        => Assert.True(AnnotationGeometry.InitialHalfPixels > AnnotationGeometry.MinimumHalfPixels);

    // ── Drawn size ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Zoomed in, the shape tracks the image: it is stored in data units, so it grows with what it
    /// encloses. This is the behaviour that must NOT be capped — a box outlines a region, and a box
    /// that stopped growing would stop outlining it.
    /// </summary>
    [Fact]
    public void ZoomingInGrowsTheShapeWithTheImage()
    {
        var mark = Box(Sky(), 0.01);

        var near = AnnotationGeometry.HalfSize(mark, new Scaled(1000), 3)!.Value;
        var far = AnnotationGeometry.HalfSize(mark, new Scaled(4000), 3)!.Value;

        Assert.Equal(10.0, near.HalfW, 6);
        Assert.Equal(40.0, far.HalfW, 6);
    }

    /// <summary>
    /// Zoomed out far enough, the same mark would be a fraction of a pixel across — present and
    /// invisible. It stops shrinking instead.
    /// </summary>
    [Fact]
    public void ZoomingOutNeverMakesAMarkDisappear()
    {
        var mark = Box(Sky(), 0.01);

        var box = AnnotationGeometry.HalfSize(mark, new Scaled(0.001), 3)!.Value;

        Assert.Equal(AnnotationGeometry.MinimumHalfPixels, box.HalfW, 6);
        Assert.Equal(AnnotationGeometry.MinimumHalfPixels, box.HalfH, 6);
    }

    /// <summary>
    /// Held at the floor, the mark still sits on the position it was pinned to. The clamp is applied
    /// symmetrically about the projected anchor, so a mark that stops shrinking does not start
    /// drifting — which is the failure that would be far harder to notice than a mark that vanished.
    /// </summary>
    [Fact]
    public void AMarkHeldAtTheFloorStaysCentredOnItsAnchor()
    {
        var mark = Box(Sky(), 1e-9);

        var tiny = AnnotationGeometry.HalfSize(mark, new Scaled(0.001), 3)!.Value;
        var large = AnnotationGeometry.HalfSize(mark, new Scaled(1e9), 3)!.Value;

        // Project() puts the anchor at the origin on this surface, at every scale.
        Assert.Equal(0.0, tiny.Cx, 9);
        Assert.Equal(0.0, tiny.Cy, 9);
        Assert.Equal(large.Cx, tiny.Cx, 9);
        Assert.Equal(large.Cy, tiny.Cy, 9);
    }

    /// <summary>
    /// The floor is in SCREEN pixels, and an export plate's pixels are smaller than the screen's — it
    /// renders several times larger. A floor left at the screen's size would be a speck on the plate,
    /// so it scales with the ink, exactly as strokes and labels do.
    /// </summary>
    [Fact]
    public void TheFloorGrowsWithAnExportPlatesInkScale()
    {
        var mark = Box(Sky(), 1e-9);

        var plate = AnnotationGeometry.HalfSize(mark, new Scaled(0.001) { InkScale = 4.0 }, 3)!.Value;

        Assert.Equal(AnnotationGeometry.MinimumHalfPixels * 4.0, plate.HalfW, 6);
    }

    /// <summary>
    /// A mark with no extent — a callout, which has no shape to size — keeps taking the caller's
    /// fallback. The floor is about shapes that got too small, not about giving one to a mark that
    /// never had a shape.
    /// </summary>
    [Fact]
    public void AMarkWithNoExtentStillUsesTheCallersFallback()
    {
        var callout = new Annotation
        {
            Id = "m2", Kind = AnnotationKind.Callout, Anchor = Sky(), Extent = null, Author = MarkAuthor.User,
        };

        var box = AnnotationGeometry.HalfSize(callout, new Scaled(1000), 3)!.Value;

        Assert.Equal(3.0, box.HalfW, 6);
        Assert.Equal(3.0, box.HalfH, 6);
    }

    /// <summary>
    /// What you can see is what you can grab. The grips are placed from the same clamped box, so a
    /// mark held at the floor is still resizable rather than being a target too small to hit.
    /// </summary>
    [Fact]
    public void TheGripsSitOnTheClampedBoxNotTheTrueOne()
    {
        var mark = Box(Sky(), 1e-9);

        var handles = AnnotationGeometry.Handles(mark, new Scaled(0.001));

        Assert.Equal(4, handles.Count);
        Assert.All(handles, h =>
            Assert.Equal(AnnotationGeometry.MinimumHalfPixels, Math.Abs(h.X), 6));
    }

    // ── Guards ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>A surface that cannot place the mark cannot size it either.</summary>
    [Fact]
    public void AMarkThatIsNotOnTheSurfaceHasNoResizeAnswer()
    {
        var offSurface = new Scaled(1.0);
        var invalid = Box(AnnotationAnchor.ImagePixel(double.NaN, 0), 1);

        Assert.Null(AnnotationGeometry.ResizeHalf(invalid, offSurface, 20, 20));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void ADegenerateScaleHasNoResizeAnswer(double pixelsPerUnit)
        => Assert.Null(AnnotationGeometry.ResizeHalf(Box(Pixel(), 1), new Scaled(pixelsPerUnit), 20, 20));

    [Fact]
    public void ADragToNowhereHasNoResizeAnswer()
        => Assert.Null(AnnotationGeometry.ResizeHalf(Box(Pixel(), 1), new Scaled(1.0), double.NaN, 20));
}
