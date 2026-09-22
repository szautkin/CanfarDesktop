using Xunit;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// The geometry every viewer's marks share. Ported from the Linux build, including the
/// substitutability contract that three shipped bugs were violations of.
/// </summary>
public class AnnotationGeometryTests
{
    /// <summary>A surface that projects image pixels straight through — the geometry needs no viewer.</summary>
    private sealed class Flat : IAnnotationSurface
    {
        public (double X, double Y)? Project(AnnotationAnchor a)
            => a.Space == AnchorSpace.ImagePixel ? (a.X, a.Y) : null;

        public double UnitsToPixels(AnnotationAnchor a) => 1.0;
    }

    /// <summary>The same surface rendering four times the size — an export plate.</summary>
    private sealed class Quadruple : IAnnotationSurface
    {
        public (double X, double Y)? Project(AnnotationAnchor a)
            => a.Space == AnchorSpace.ImagePixel ? (a.X * 4, a.Y * 4) : null;

        public double UnitsToPixels(AnnotationAnchor a) => 4.0;
        public double InkScale => 4.0;
    }

    private static Annotation Circle(string id, double x, double y, double half = 10) => new()
    {
        Id = id,
        Kind = AnnotationKind.Circle,
        Anchor = AnnotationAnchor.ImagePixel(x, y),
        Extent = Extent.Square(half),
    };

    // ── The substitutability contract ───────────────────────────────────────────────────────────

    /// <summary>
    /// Every surface must agree what a mark's size MEANS. Three shipped bugs were violations of this:
    /// a mark that kept its screen size in a 4× export, marks missing from an exported figure, and a
    /// mark with no radius drawn invisibly. It is one contract, so it is one test, run against every
    /// implementation — including the ones added later.
    /// </summary>
    public static TheoryData<string, IAnnotationSurface, double> Surfaces() => new()
    {
        { "screen", new Flat(), 1.0 },
        { "4x export plate", new Quadruple(), 4.0 },
    };

    [Theory]
    [MemberData(nameof(Surfaces))]
    public void AShapeOccupiesTheSurfacesOwnScale(string name, IAnnotationSurface surface, double factor)
    {
        var mark = Circle("m", 100, 100, half: 10);
        var box = AnnotationGeometry.HalfSize(mark, surface, 8.0);

        Assert.True(box.HasValue, name);
        Assert.Equal(10 * factor, box!.Value.HalfW, 6);
        Assert.Equal(10 * factor, box.Value.HalfH, 6);
    }

    [Theory]
    [MemberData(nameof(Surfaces))]
    public void TheGripsSitOnTheCornersOfTheShapeOnEverySurface(string name, IAnnotationSurface surface, double factor)
    {
        var mark = Circle("m", 100, 100, half: 10);
        var handles = AnnotationGeometry.Handles(mark, surface);

        Assert.Equal(4, handles.Count);
        Assert.NotEmpty(name);
        var centre = surface.Project(mark.Anchor)!.Value;
        Assert.All(handles, h =>
        {
            Assert.Equal(10 * factor, Math.Abs(h.X - centre.X), 6);
            Assert.Equal(10 * factor, Math.Abs(h.Y - centre.Y), 6);
        });
    }

    /// <summary>
    /// The ink factor is what makes a 4× export readable. Left at 1.0, a 2px ring stays 2px on a plate
    /// whose title and colorbar have quadrupled — the annotations become the only thing that shrank.
    /// </summary>
    [Fact]
    public void AnExportSurfaceScalesItsFurnitureToo()
    {
        var screen = AnnotationGeometry.LeaderGeometry(100, 100, 10, 10, true, null, 40, 1000, 1.0);
        var plate = AnnotationGeometry.LeaderGeometry(400, 400, 40, 40, true, null, 160, 4000, 4.0);

        var screenReach = Math.Abs(screen.ElbowX - screen.StartX);
        var plateReach = Math.Abs(plate.ElbowX - plate.StartX);

        Assert.Equal(4.0, plateReach / screenReach, 6);
    }

    // ── The leader ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The leader starts ON the outline, not at the bounding-box corner (outside a circle) and not at
    /// the centre (a line drawn through the subject).
    /// </summary>
    [Fact]
    public void ALeaderLeavesTheOutlineOfACircleNotItsBoundingBox()
    {
        var leader = AnnotationGeometry.LeaderGeometry(
            cx: 100, cy: 100, halfW: 20, halfH: 20, elliptical: true,
            offset: null, textWidth: 30, canvasWidth: 1000, ink: 1.0);

        var distance = Math.Sqrt(Math.Pow(leader.StartX - 100, 2) + Math.Pow(leader.StartY - 100, 2));
        Assert.Equal(20.0, distance, 6);   // exactly on the radius
    }

    [Fact]
    public void ALeaderLeavesABoxThroughItsEdge()
    {
        var leader = AnnotationGeometry.LeaderGeometry(
            cx: 100, cy: 100, halfW: 20, halfH: 5, elliptical: false,
            offset: null, textWidth: 30, canvasWidth: 1000, ink: 1.0);

        // At 45° the short axis is met first, so the start is on the top edge.
        Assert.Equal(95.0, leader.StartY, 6);
        Assert.True(leader.StartX < 120);
    }

    /// <summary>
    /// The length is measured FROM the outline. Measured from the centre, a shape bigger than the
    /// leader swallowed it and the line read as crossing the circle rather than leaving it.
    /// </summary>
    [Fact]
    public void TheLeaderLengthIsMeasuredFromTheOutline()
    {
        var small = AnnotationGeometry.LeaderGeometry(100, 100, 5, 5, true, null, 30, 1000, 1.0);
        var large = AnnotationGeometry.LeaderGeometry(100, 100, 60, 60, true, null, 30, 1000, 1.0);

        double Reach(AnnotationGeometry.Leader l)
            => Math.Sqrt(Math.Pow(l.ElbowX - l.StartX, 2) + Math.Pow(l.ElbowY - l.StartY, 2));

        Assert.Equal(Reach(small), Reach(large), 6);
    }

    /// <summary>The whole thing flips to whichever side has room.</summary>
    [Fact]
    public void ALeaderFlipsAwayFromTheEdgeItWouldRunOff()
    {
        var roomy = AnnotationGeometry.LeaderGeometry(100, 100, 10, 10, true, null, 40, 1000, 1.0);
        var cramped = AnnotationGeometry.LeaderGeometry(980, 100, 10, 10, true, null, 40, 1000, 1.0);

        Assert.True(roomy.Rightwards);
        Assert.False(cramped.Rightwards);
        Assert.True(cramped.RuleEndX < cramped.ElbowX);
    }

    // ── Hit-testing ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheTopmostMarkUnderThePointerIsTheOneYouGet()
    {
        List<Annotation> marks = [Circle("under", 100, 100), Circle("over", 100, 100)];

        Assert.Equal("over", AnnotationGeometry.AnnotationAt(marks, new Flat(), 100, 100));
    }

    /// <summary>
    /// A mark that cannot be placed is SKIPPED, not fatal. Abandoning the search on the first one made
    /// a single mark anchored in another viewer's space render every other mark unclickable.
    /// </summary>
    [Fact]
    public void AnUnplaceableMarkDoesNotHideTheOnesUnderIt()
    {
        var elsewhere = new Annotation
        {
            Id = "cube",
            Kind = AnnotationKind.Circle,
            Anchor = AnnotationAnchor.Data(1, 2, 3),   // the Flat surface cannot project this
            Extent = Extent.Square(10),
        };
        List<Annotation> marks = [Circle("flat", 100, 100), elsewhere];

        Assert.Equal("flat", AnnotationGeometry.AnnotationAt(marks, new Flat(), 100, 100));
    }

    /// <summary>A hairline circle a few pixels across is impossible to hit exactly, so the target has a floor.</summary>
    [Fact]
    public void ATinyMarkIsStillClickable()
    {
        List<Annotation> marks = [Circle("tiny", 100, 100, half: 0.5)];

        Assert.Equal("tiny", AnnotationGeometry.AnnotationAt(marks, new Flat(), 104, 100));
        Assert.Null(AnnotationGeometry.AnnotationAt(marks, new Flat(), 130, 100));
    }

    [Fact]
    public void AMarkWithNoExtentHasNoGrips()
    {
        var text = new Annotation
        {
            Id = "t", Kind = AnnotationKind.Text, Text = "note",
            Anchor = AnnotationAnchor.ImagePixel(100, 100),
        };

        Assert.Empty(AnnotationGeometry.Handles(text, new Flat()));
        Assert.False(AnnotationGeometry.HandleAt(text, new Flat(), 100, 100));
    }

    // ── What a press means ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A grip beats the shape it sits on. Grips are ON the outline, so testing the shape first would
    /// mean a grip could never be grabbed and resizing would look simply broken.
    /// </summary>
    [Fact]
    public void AGripOfTheEditedMarkWinsOverItsOwnShape()
    {
        var mark = Circle("m", 100, 100, half: 20);
        var grab = AnnotationGeometry.GrabAt([mark], new Flat(), activeId: "m", drawing: false, sx: 120, sy: 120);

        Assert.Equal(new MarkGrab.Resize("m"), grab);
    }

    /// <summary>
    /// A small selected mark can still be MOVED.
    ///
    /// <para>The grips are at the four corners with nine pixels of reach each, so once a mark is
    /// drawn near its minimum size those four squares blanket the whole shape and there is no part of
    /// it left that means "move". The mark could be dragged freely until you selected it, and then
    /// every drag resized it instead — which is the cube's slice view in normal use, because a
    /// mark's size is stored in voxels and shrinks on screen as you zoom out.</para>
    /// </summary>
    [Fact]
    public void ASmallSelectedMarkCanStillBeMoved()
    {
        var mark = Circle("m", 100, 100, half: 4);
        var grab = AnnotationGeometry.GrabAt([mark], new Flat(), activeId: "m", drawing: false, sx: 100, sy: 100);

        var move = Assert.IsType<MarkGrab.Move>(grab);
        Assert.Equal("m", move.Id);
    }

    /// <summary>
    /// And it can still be RESIZED. The core buys back the middle, not the corners — a fix that made
    /// small marks movable by making them unresizable would just be the same bug facing the other way.
    /// </summary>
    [Fact]
    public void ASmallSelectedMarkCanStillBeResized()
    {
        var mark = Circle("m", 100, 100, half: 4);
        var grab = AnnotationGeometry.GrabAt([mark], new Flat(), activeId: "m", drawing: false, sx: 106, sy: 106);

        Assert.Equal(new MarkGrab.Resize("m"), grab);
    }

    /// <summary>
    /// The core does not reach outside the mark it belongs to, so it cannot swallow a press on the
    /// canvas beside a small mark — that press still means "put one down" or "let go".
    /// </summary>
    [Fact]
    public void TheCoreStaysInsideItsOwnMark()
    {
        var mark = Circle("m", 100, 100, half: 4);

        Assert.False(AnnotationGeometry.CoreAt(mark, new Flat(), 140, 140));
        Assert.True(AnnotationGeometry.CoreAt(mark, new Flat(), 100, 100));
    }

    /// <summary>
    /// Drawing armed is checked LAST. Checked first, every press made a new mark — so a mark could not
    /// be moved without disarming the pencil, and pressing on the mark you were editing dropped another
    /// one on top of it.
    /// </summary>
    [Fact]
    public void WithThePencilArmedAPressOnAMarkStillTakesHoldOfIt()
    {
        var mark = Circle("m", 100, 100, half: 20);
        var grab = AnnotationGeometry.GrabAt([mark], new Flat(), activeId: null, drawing: true, sx: 100, sy: 100);

        var move = Assert.IsType<MarkGrab.Move>(grab);
        Assert.Equal("m", move.Id);
    }

    /// <summary>The grab offset is where in the shape it was taken hold of, so it does not jump.</summary>
    [Fact]
    public void MovingAMarkRemembersWhereItWasGrabbed()
    {
        var mark = Circle("m", 100, 100, half: 20);
        var move = Assert.IsType<MarkGrab.Move>(
            AnnotationGeometry.GrabAt([mark], new Flat(), null, false, 110, 95));

        Assert.Equal(10, move.GrabDx, 6);
        Assert.Equal(-5, move.GrabDy, 6);
    }

    [Fact]
    public void EmptySpaceIsThePencilsOrTheCanvass()
    {
        Assert.IsType<MarkGrab.Place>(AnnotationGeometry.GrabAt([], new Flat(), null, drawing: true, 10, 10));
        Assert.IsType<MarkGrab.None>(AnnotationGeometry.GrabAt([], new Flat(), null, drawing: false, 10, 10));
    }

    // ── Drags ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ADragIsMeasuredOnScreenAndConvertedByTheLocalScale()
    {
        var anchor = AnnotationAnchor.ImagePixel(100, 100);

        Assert.Equal(40, AnnotationGeometry.HalfFromDrag(new Flat(), anchor, 40), 6);
        // The same drag on a 4x surface asks for a quarter as much DATA.
        Assert.Equal(10, AnnotationGeometry.HalfFromDrag(new Quadruple(), anchor, 40), 6);
    }

    /// <summary>
    /// The grip is a corner, so a resize takes the LARGER offset — dragging away from the centre grows
    /// the shape whichever way you go.
    /// </summary>
    [Fact]
    public void AResizeTakesTheLargerOfTheTwoOffsets()
    {
        var mark = Circle("m", 100, 100, half: 10);

        Assert.Equal(30, AnnotationGeometry.ResizeHalf(mark, new Flat(), 130, 105)!.Value, 6);
        Assert.Equal(30, AnnotationGeometry.ResizeHalf(mark, new Flat(), 105, 70)!.Value, 6);
    }

    /// <summary>
    /// A resize never produces a shape with no area — that is a mark that has vanished.
    ///
    /// The floor is in SCREEN pixels, so on this surface's 1 pixel per unit it reads back unchanged.
    /// It used to be half a UNIT, which on a sky anchor meant half a degree and produced boxes wider
    /// than the image — see MarkSizingTests.
    /// </summary>
    [Fact]
    public void AResizeToNothingStillLeavesSomethingVisible()
        => Assert.Equal(
            AnnotationGeometry.MinimumHalfPixels,
            AnnotationGeometry.ResizeHalf(Circle("m", 100, 100), new Flat(), 100, 100)!.Value, 6);

    // ── The words are part of the mark ──────────────────────────────────────────────────────────

    /// <summary>
    /// A label sits at the end of a leader, which can be well away from its shape. A label that could
    /// not be clicked would be a visible piece of the mark that is inert — which reads as the click
    /// being broken rather than as a rule.
    /// </summary>
    [Fact]
    public void ClickingTheWordsPicksTheMark()
    {
        var mark = Circle("m1", 50, 50, 5) with { Text = "NGC 4321" };
        var surface = new Flat();

        var label = AnnotationGeometry.LabelBox(mark, surface);
        Assert.NotNull(label);

        var midX = (label!.Value.Left + label.Value.Right) / 2;
        var midY = (label.Value.Top + label.Value.Bottom) / 2;

        Assert.Equal("m1", AnnotationGeometry.AnnotationAt([mark], surface, midX, midY));
    }

    /// <summary>The label is up and to the right of the shape, so it is genuinely a separate target.</summary>
    [Fact]
    public void TheLabelIsClearOfTheShapeItBelongsTo()
    {
        var mark = Circle("m1", 50, 50, 5) with { Text = "NGC 4321" };

        var label = AnnotationGeometry.LabelBox(mark, new Flat())!.Value;

        Assert.True(label.Left > 50, "the label should sit to the right of its shape");
        Assert.True(label.Bottom < 50, "the label should sit above its shape");
    }

    [Fact]
    public void AMarkWithNoWordsHasNoLabelToClick()
        => Assert.Null(AnnotationGeometry.LabelBox(Circle("m1", 50, 50, 5), new Flat()));

    [Fact]
    public void WhitespaceIsNotALabel()
        => Assert.Null(AnnotationGeometry.LabelBox(
            Circle("m1", 50, 50, 5) with { Text = "   " }, new Flat()));

    /// <summary>Empty space is still empty: a hit box that swallows the canvas would stop panning.</summary>
    [Fact]
    public void ClickingWellAwayFromBothStillHitsNothing()
    {
        var mark = Circle("m1", 50, 50, 5) with { Text = "NGC 4321" };

        Assert.Null(AnnotationGeometry.AnnotationAt([mark], new Flat(), 400, 400));
    }

    /// <summary>The shape still wins where they overlap — it is what a person aimed at.</summary>
    [Fact]
    public void TheShapeIsStillHitAtItsOwnCentre()
    {
        var mark = Circle("m1", 50, 50, 5) with { Text = "NGC 4321" };

        Assert.Equal("m1", AnnotationGeometry.AnnotationAt([mark], new Flat(), 50, 50));
    }

    /// <summary>
    /// A longer label is a wider target. The renderer lays the rule out from this same estimate, so a
    /// hit box computed any other way would creep away from the words as the label grows.
    /// </summary>
    [Fact]
    public void ALongerLabelIsAWiderTarget()
    {
        var surface = new Flat();
        var shortish = AnnotationGeometry.LabelBox(Circle("m1", 50, 50, 5) with { Text = "a" }, surface)!.Value;
        var longer = AnnotationGeometry.LabelBox(Circle("m1", 50, 50, 5) with { Text = "a much longer label" }, surface)!.Value;

        Assert.True(longer.Right - longer.Left > shortish.Right - shortish.Left);
    }

    [Fact]
    public void BoldTextIsMeasuredWider()
        => Assert.True(AnnotationGeometry.EstimateTextWidth("abc", 12, bold: true)
                     > AnnotationGeometry.EstimateTextWidth("abc", 12, bold: false));

    [Fact]
    public void NoTextHasNoWidth()
        => Assert.Equal(0, AnnotationGeometry.EstimateTextWidth(null, 12, bold: false));

    // ── Resizing a mark you have picked out ─────────────────────────────────────────────────────

    /// <summary>
    /// Selecting a mark is what makes its grips live.
    ///
    /// They used to belong to the mark being EDITED, which is only so while its label field is open —
    /// so a mark could be resized in the moment it was drawn and never again. Clicking it afterwards
    /// selected it, no grip answered, and dragging the outline moved it instead. That is what "cannot
    /// resize the box" looked like, in both viewers, because both ask this one function.
    /// </summary>
    [Fact]
    public void AGripOfTheSelectedMarkResizesIt()
    {
        var mark = Circle("m", 100, 100, half: 10);

        // A grip sits at a CORNER of the bounding box, not the middle of an edge.
        var grab = AnnotationGeometry.GrabAt([mark], new Flat(), activeId: "m", drawing: false, sx: 110, sy: 110);

        Assert.Equal("m", Assert.IsType<MarkGrab.Resize>(grab).Id);
    }

    /// <summary>A mark nobody has picked out is moved by its outline, not resized.</summary>
    [Fact]
    public void TheOutlineOfAnUnselectedMarkMovesIt()
    {
        var mark = Circle("m", 100, 100, half: 10);

        var grab = AnnotationGeometry.GrabAt([mark], new Flat(), activeId: null, drawing: false, sx: 110, sy: 110);

        Assert.Equal("m", Assert.IsType<MarkGrab.Move>(grab).Id);
    }

    /// <summary>
    /// Only the picked-out mark's grips answer. Every mark offering grips would mean a press between
    /// two overlapping marks resized whichever was found first, which is not a thing anybody asked for.
    /// </summary>
    [Fact]
    public void AnotherMarksGripDoesNotAnswer()
    {
        List<Annotation> marks = [Circle("a", 100, 100, half: 10), Circle("b", 300, 300, half: 10)];

        var grab = AnnotationGeometry.GrabAt(marks, new Flat(), activeId: "b", drawing: false, sx: 110, sy: 110);

        Assert.IsType<MarkGrab.Move>(grab);
    }

    /// <summary>
    /// A grip still wins over the outline it sits on. Grips are ON the shape, so testing the shape
    /// first would mean a grip could never be grabbed and resizing would look simply broken.
    /// </summary>
    [Fact]
    public void AGripBeatsTheOutlineItSitsOn()
    {
        var mark = Circle("m", 100, 100, half: 10);

        Assert.IsType<MarkGrab.Resize>(
            AnnotationGeometry.GrabAt([mark], new Flat(), activeId: "m", drawing: true, sx: 110, sy: 110));
    }
}
