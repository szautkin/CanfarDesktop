using CanfarDesktop.Models;

namespace CanfarDesktop.Helpers;

/// <summary>
/// What a viewer has to answer for its marks to be drawn on it.
///
/// Three methods, and they are the ONLY thing the FITS canvas, the cube's volume view, its slice view
/// and an export plate differ by. Everything else — the shapes, the leader geometry, the hit-testing,
/// what a press means — is the same code in all four places.
/// </summary>
public interface IAnnotationSurface
{
    /// <summary>The anchor's position on the surface, in device pixels. Null when it is not on it at all.</summary>
    (double X, double Y)? Project(AnnotationAnchor anchor);

    /// <summary>
    /// How many device pixels one unit of the anchor's space currently spans.
    ///
    /// A shape's size is stored in data units, so it grows and shrinks with the view the way a circle
    /// drawn on a photograph does. The viewer knows the scale; the renderer only knows it needs one.
    /// </summary>
    double UnitsToPixels(AnnotationAnchor anchor);

    /// <summary>
    /// How much bigger this rendering is than the screen. 1.0 IS the screen.
    ///
    /// A mark's stroke, label and leader are in DEVICE pixels, deliberately: a stroke that thickened as
    /// you zoomed out would turn the view into a blot. But "device pixels" means the SCREEN's, and an
    /// export at 4× has four times as many of them.
    ///
    /// Left at 1.0 everywhere, that is a measured bug rather than a hypothetical one: at 4× a 2px ring
    /// stayed 2px and a 12px label stayed a smudge, on a plate whose own title, caption and colorbar
    /// DID scale — so the annotations were the only thing in the figure that shrank, and the marks
    /// became unreadable at exactly the resolution someone chose for publication.
    ///
    /// Every surface that renders bigger than the screen answers with its factor. The default is the screen.
    /// </summary>
    double InkScale => 1.0;
}

/// <summary>What a press on a canvas is asking for.</summary>
public abstract record MarkGrab
{
    /// <summary>Nothing of ours is under the pointer; the canvas can have the event.</summary>
    public sealed record None : MarkGrab;

    /// <summary>
    /// Move this mark. <paramref name="GrabDx"/>/<paramref name="GrabDy"/> are where in the shape it was
    /// taken hold of, so it does not jump to centre itself under the pointer.
    /// </summary>
    public sealed record Move(string Id, double GrabDx, double GrabDy) : MarkGrab;

    /// <summary>Resize this mark by the grip that was grabbed.</summary>
    public sealed record Resize(string Id) : MarkGrab;

    /// <summary>Nothing was under the pointer and drawing is armed: make a new mark.</summary>
    public sealed record Place : MarkGrab;
}

/// <summary>
/// The geometry every viewer's marks share: where a leader goes, where the grips are, what is under the
/// pointer, and what a drag is asking for.
///
/// Free of any drawing API on purpose. The FITS canvas draws with XAML shapes and an export plate draws
/// into a bitmap, but neither of them should be the place that decides where a leader elbow sits — that
/// is one answer, and two copies of it drift.
/// </summary>
public static class AnnotationGeometry
{
    /// <summary>A mark being edited or picked out is drawn thicker, whatever its own stroke says.</summary>
    public const double SelectedStroke = 2.0;

    /// <summary>
    /// The leader leaves a shape at this angle, and every leader on a canvas uses the same one —
    /// varying angles is what makes an annotated figure look untidy.
    /// </summary>
    public const double LeaderAngleDegrees = 45.0;

    /// <summary>Default leader length in pixels, when a callout has no stored offset.</summary>
    public const double LeaderLength = 46.0;

    /// <summary>Gap between the rule and the text sitting on it.</summary>
    public const double TextLift = 3.0;

    /// <summary>A little slack past the text, so the rule is never exactly flush.</summary>
    public const double RuleOverhang = 6.0;

    public const double HandleRadius = 5.0;

    /// <summary>
    /// The smallest half-size a mark is ever drawn or dragged out at, in DEVICE pixels.
    ///
    /// <para>In device pixels, and nowhere near the anchor's own units, because a floor written in
    /// those units means something different in each space and something catastrophic in one of them.
    /// A floor of half a unit is half a voxel to a cube — and half a DEGREE to a sky-anchored mark. On
    /// a 0.187 arcsec/pixel image that is a box 19,000 pixels across, four times wider than the whole
    /// frame, which is exactly the "I drew a small box and got a gigantic one" report: every mark came
    /// out at the floor no matter how small the drag.</para>
    ///
    /// <para>A screen-pixel floor is the same promise in every space — a mark you can see and grab —
    /// and the conversion to the anchor's units is <see cref="HalfFromDrag"/>, which already exists and
    /// is already the one place that knows how.</para>
    /// </summary>
    public const double MinimumHalfPixels = 4.0;

    /// <summary>
    /// The half-size, in DEVICE pixels, a shape is born at before the drag that sizes it.
    ///
    /// A mark placed with no extent is invisible until the drag ends, so a press that starts a drag
    /// looks like it did nothing. Comfortably above <see cref="MinimumHalfPixels"/>, so a mark that is
    /// clicked rather than dragged is a shape someone meant to make rather than a speck.
    /// </summary>
    public const double InitialHalfPixels = 12.0;

    /// <summary>
    /// The part of a selected mark that always takes hold of it, in DEVICE pixels, whatever the grips
    /// are doing.
    ///
    /// <para>A grip is a corner with <c>HandleRadius + 4</c> of reach, and there are four of them. On a
    /// mark drawn near <see cref="MinimumHalfPixels"/> those four squares blanket the entire shape and
    /// overlap in the middle of it, so there was no part of a small mark left that meant "move". It
    /// could be dragged while unselected — no grips — and the moment it was picked out, every drag
    /// resized it instead.</para>
    ///
    /// <para>That is the cube's slice view in ordinary use: a mark's size is kept in voxels, so it
    /// shrinks on screen as you zoom out, and marks there routinely sit at the floor. A flat image
    /// hides it because the same mark is usually far bigger in screen pixels.</para>
    ///
    /// <para>Small enough that it never steals a corner on a mark big enough to have real ones, and
    /// large enough to hit: the middle of a selected shape moving it is what every drawing tool
    /// does.</para>
    /// </summary>
    public const double CoreHalfPixels = 4.0;

    /// <summary>The four corner offsets a grip sits at.</summary>
    private static readonly (double Dx, double Dy)[] HandleCorners = [(-1, -1), (1, -1), (1, 1), (-1, 1)];

    /// <summary>Where a callout's leader, rule and text go, all in device pixels.</summary>
    public sealed record Leader(
        double StartX, double StartY,
        double ElbowX, double ElbowY,
        double RuleEndX, double TextX,
        bool Rightwards);

    /// <summary>
    /// The leader from a shape to its label.
    ///
    /// * The start is on the OUTLINE, found along the leader's own direction — not the bounding-box
    ///   corner, which for a circle is outside it, and not the centre, which draws a line through the
    ///   subject.
    /// * The length is measured FROM that start. Measured from the centre, a shape bigger than the
    ///   leader swallowed it and the elbow came out a few pixels from the outline, so the line read as
    ///   crossing the circle rather than leaving it.
    /// * The rule is as long as its text, and the whole thing flips to whichever side has room.
    /// </summary>
    public static Leader LeaderGeometry(
        double cx, double cy, double halfW, double halfH, bool elliptical,
        (double Dx, double Dy)? offset, double textWidth, double canvasWidth, double ink)
    {
        var angle = LeaderAngleDegrees * Math.PI / 180.0;
        var (rawDx, rawDy) = offset ?? (Math.Cos(angle), -Math.Sin(angle));
        var length = Math.Max(Math.Sqrt(rawDx * rawDx + rawDy * rawDy), double.Epsilon);
        var ux = rawDx / length;
        var uy = rawDy / length;

        // Every length here is furniture in device pixels, so every one takes the ink factor.
        // `textWidth` arrives already scaled, because it was measured with the scaled font — mixing a
        // scaled width with an unscaled overhang is how a rule ends up not reaching the end of its text.
        var ruleLength = textWidth + RuleOverhang * ink;

        // The stored offset is in screen pixels too — a label is furniture, not part of the image — so
        // it scales with the rest of the furniture.
        var leaderLength = offset is not null
            ? Math.Max(length * ink, LeaderLength * 0.5 * ink)
            : LeaderLength * ink;

        var reach = halfW + leaderLength + ruleLength;
        var rightwards = ux >= 0 ? cx + reach <= canvasWidth : cx - reach < 0;
        ux = rightwards ? Math.Abs(ux) : -Math.Abs(ux);

        // The point where the leader leaves the outline.
        double sx, sy;
        if (elliptical)
        {
            sx = cx + halfW * ux;
            sy = cy + halfH * uy;
        }
        else
        {
            // Ray/box intersection: scale the direction until it meets an edge.
            var tx = Math.Abs(ux) > double.Epsilon ? halfW / Math.Abs(ux) : double.MaxValue;
            var ty = Math.Abs(uy) > double.Epsilon ? halfH / Math.Abs(uy) : double.MaxValue;
            var t = Math.Min(tx, ty);
            sx = cx + ux * t;
            sy = cy + uy * t;
        }

        var elbowX = sx + ux * leaderLength;
        var elbowY = sy + uy * leaderLength;
        var ruleEnd = rightwards ? elbowX + ruleLength : elbowX - ruleLength;
        var textX = rightwards
            ? elbowX + RuleOverhang * ink / 2.0
            : ruleEnd + RuleOverhang * ink / 2.0;

        return new Leader(sx, sy, elbowX, elbowY, ruleEnd, textX, rightwards);
    }

    /// <summary>
    /// A mark's centre and half-size on screen, or null when it is not on this surface.
    ///
    /// <para>A mark's size is stored in the anchor's units, so it tracks the image the way a circle
    /// drawn on a photograph does: zoom in and it grows with what it encloses. Zoom far enough OUT,
    /// though, and a small mark is a fraction of a pixel across — present, hit-testable in principle,
    /// and invisible. So the drawn size is floored at <see cref="MinimumHalfPixels"/>.</para>
    ///
    /// <para>The floor is applied symmetrically about the projected anchor, so a mark held at the
    /// minimum still sits exactly on the position it was pinned to; it stops shrinking, it does not
    /// drift. And it is floored HERE rather than in each viewer's renderer, so what you can see and
    /// what you can grab are the same rectangle — hit-testing and the grips read this too.</para>
    /// </summary>
    public static (double Cx, double Cy, double HalfW, double HalfH)? HalfSize(
        Annotation mark, IAnnotationSurface surface, double fallback)
    {
        if (surface.Project(mark.Anchor) is not { } centre) return null;

        var scale = surface.UnitsToPixels(mark.Anchor);
        var floor = MinimumHalfPixels * UsableInkScale(surface);

        var (hw, hh) = mark.Extent is { } e
            ? (Math.Max(e.HalfWidth * scale, floor), Math.Max(e.HalfHeight * scale, floor))
            : (fallback, fallback);

        return (centre.X, centre.Y, hw, hh);
    }

    /// <summary>
    /// A surface's ink scale, guarded. An export plate renders bigger than the screen and says so, and
    /// a minimum size in SCREEN pixels has to grow with it or the floor is invisible on the plate.
    /// </summary>
    private static double UsableInkScale(IAnnotationSurface surface)
        => double.IsFinite(surface.InkScale) && surface.InkScale > 0 ? surface.InkScale : 1.0;

    /// <summary>
    /// Where the four resize grips are. Screen-sized, not data-sized: a grip has to be grabbable at any
    /// zoom, and one that shrank with the image would become unusable exactly when a mark is small
    /// enough to need adjusting.
    /// </summary>
    public static IReadOnlyList<(double X, double Y)> Handles(Annotation mark, IAnnotationSurface surface)
    {
        if (mark.Extent is null) return [];
        if (HalfSize(mark, surface, 3.0) is not { } box) return [];

        return HandleCorners
            .Select(c => (box.Cx + c.Dx * box.HalfW, box.Cy + c.Dy * box.HalfH))
            .ToList();
    }

    /// <summary>Whether a point is on one of a mark's grips.</summary>
    public static bool HandleAt(Annotation mark, IAnnotationSurface surface, double sx, double sy)
    {
        // A little larger than it looks: a 5px dot is hard to hit exactly, and a near miss that pans
        // the image instead is infuriating.
        const double reach = HandleRadius + 4.0;

        return Handles(mark, surface).Any(h => Math.Abs(sx - h.X) <= reach && Math.Abs(sy - h.Y) <= reach);
    }

    /// <summary>
    /// Whether a point is in the middle of a mark — the part that moves it however small it is.
    ///
    /// Measured against the DRAWN size, which is floored at <see cref="MinimumHalfPixels"/> — so with
    /// today's constants the core is exactly that floor and the Math.Min below changes nothing. It is
    /// there to keep the rule true if the floor is ever lowered: a core bigger than its own mark would
    /// claim presses on the empty canvas beside it.
    /// </summary>
    public static bool CoreAt(Annotation mark, IAnnotationSurface surface, double sx, double sy)
    {
        if (HalfSize(mark, surface, MinimumHalfPixels) is not { } box) return false;

        var core = Math.Min(CoreHalfPixels, Math.Max(box.HalfW, box.HalfH));
        return Math.Abs(sx - box.Cx) <= core && Math.Abs(sy - box.Cy) <= core;
    }

    /// <summary>
    /// The topmost mark whose shape covers a point. Last drawn is tested first, so the mark on top is
    /// the one you get.
    /// </summary>
    public static string? AnnotationAt(
        IReadOnlyList<Annotation> annotations, IAnnotationSurface surface, double sx, double sy)
    {
        for (var i = annotations.Count - 1; i >= 0; i--)
        {
            // Skip, do not abandon: a mark that cannot be placed — one anchored in another viewer's
            // space, or off this image — used to end the whole search, so a single unplaceable mark
            // made every other mark unclickable.
            if (HalfSize(annotations[i], surface, 8.0) is not { } box) continue;

            // A generous minimum: a hairline circle a few pixels across is impossible to hit exactly,
            // and a near miss reads as broken.
            var hw = Math.Max(box.HalfW, 6.0);
            var hh = Math.Max(box.HalfH, 6.0);

            if (Math.Abs(sx - box.Cx) <= hw && Math.Abs(sy - box.Cy) <= hh)
                return annotations[i].Id;

            // The words count as part of the mark. They sit at the end of a leader, which can be well
            // away from the shape — so a label that could not be clicked would be a piece of the mark
            // visibly there and inert, which reads as the click being broken rather than as a rule.
            if (LabelBox(annotations[i], surface) is { } label
                && sx >= label.Left && sx <= label.Right && sy >= label.Top && sy <= label.Bottom)
                return annotations[i].Id;
        }
        return null;
    }

    /// <summary>
    /// Where a mark's words are on screen, or null when it has none.
    ///
    /// Uses the same estimated text width the renderer lays the rule out from, because the hit box and
    /// the drawn rule have to agree: measured one way and drawn another, the clickable area creeps away
    /// from the words as a label gets longer.
    /// </summary>
    public static (double Left, double Top, double Right, double Bottom)? LabelBox(
        Annotation mark, IAnnotationSurface surface)
    {
        if (string.IsNullOrWhiteSpace(mark.Text)) return null;
        if (surface.Project(mark.Anchor) is not { } centre) return null;

        var ink = MarkStyle.UsableInk(surface.InkScale);
        var style = mark.EffectiveStyle;
        var fontSize = style.FontSize * ink;
        var width = EstimateTextWidth(mark.Text, fontSize, style.Bold);

        // A text mark has no leader: its words sit at the anchor.
        if (mark.Kind == AnnotationKind.Text)
            return (centre.X, centre.Y - fontSize * 2, centre.X + width, centre.Y);

        var box = HalfSize(mark, surface, 8.0);
        var (hw, hh) = mark.Extent is not null
            ? (Math.Max(box?.HalfW ?? 1, 1), Math.Max(box?.HalfH ?? 1, 1))
            : (3.0, 3.0);

        var offset = mark.LabelOffsetX is { } dx && mark.LabelOffsetY is { } dy ? (dx, dy) : ((double, double)?)null;

        // Canvas width only decides whether a rule near the right edge flips to the left, and a hit box
        // that is one flip out is better than refusing to test the label at all — so this asks for the
        // un-flipped geometry rather than making every caller carry a width it does not otherwise need.
        var leader = LeaderGeometry(centre.X, centre.Y, hw, hh,
            elliptical: mark.Kind != AnnotationKind.Rect, offset, width, double.MaxValue, ink);

        var top = leader.ElbowY - fontSize - TextLift * ink;
        return (leader.TextX, top, leader.TextX + width, top + fontSize);
    }

    /// <summary>
    /// A text width without laying the text out.
    ///
    /// WinUI can measure exactly, but only once the element is in the tree — and the leader's geometry
    /// has to be known BEFORE the label is placed. Deliberately a little generous, which errs towards a
    /// rule slightly longer than its text and a hit box slightly larger than its words: of the two ways
    /// to be wrong, that is the one nobody notices.
    /// </summary>
    public static double EstimateTextWidth(string? text, double fontSize, bool bold)
        => (text?.Length ?? 0) * fontSize * (bold ? 0.60 : 0.55);

    /// <summary>
    /// Decide what a press is asking for. The ORDER is the whole content of this function, and it lives
    /// here so that two canvases cannot disagree about it:
    ///
    /// 1. A grip of the mark that is picked out. Grips sit ON the outline of their own shape, so testing
    ///    the shape first would mean a grip could never be grabbed and resizing would look simply broken.
    /// 2. Any mark's shape — take hold of it and move it.
    /// 3. Empty space, with drawing armed — make a new one.
    /// 4. Empty space — the canvas can have the press.
    ///
    /// Drawing being armed is checked LAST rather than first. Checked first, every press made a new
    /// mark, so a mark could not be moved or resized without disarming the pencil — and pressing on the
    /// mark you were in the middle of editing dropped another one on top of it.
    /// </summary>
    /// <param name="activeId">
    /// The mark whose grips are live — the SELECTED one.
    ///
    /// It used to be the one being edited, which is only true while its label field is open. So a mark
    /// could be resized in the moment it was drawn and never again: clicking it selected it, no grips
    /// appeared, and dragging the outline moved it instead. Selection is what the panel's own hint
    /// promises ("click a mark to edit it: drag a grip to resize"), and it is what a person means by
    /// picking something out.
    /// </param>
    public static MarkGrab GrabAt(
        IReadOnlyList<Annotation> annotations, IAnnotationSurface surface,
        string? activeId, bool drawing, double sx, double sy)
    {
        var active = activeId is null
            ? null
            : annotations.FirstOrDefault(a => a.Id == activeId);

        // The core first, because a grip that has swallowed the whole mark leaves nothing to grab it
        // by. Corners still win over the shape everywhere else — see AGripOfTheEditedMarkWinsOverItsOwnShape.
        if (active is not null && !CoreAt(active, surface, sx, sy) && HandleAt(active, surface, sx, sy))
            return new MarkGrab.Resize(active.Id);

        if (AnnotationAt(annotations, surface, sx, sy) is { } id)
        {
            var centre = annotations.FirstOrDefault(a => a.Id == id) is { } hit
                ? surface.Project(hit.Anchor)
                : null;

            return centre is { } c
                ? new MarkGrab.Move(id, sx - c.X, sy - c.Y)
                : new MarkGrab.Move(id, 0, 0);
        }

        return drawing ? new MarkGrab.Place() : new MarkGrab.None();
    }

    /// <summary>
    /// The half-size, in the anchor's own units, that a drag of <paramref name="screenPixels"/> away
    /// from the anchor is asking for.
    ///
    /// Measured on SCREEN and divided by the local scale, rather than by unprojecting the two ends of
    /// the drag and measuring between them. On a foreshortened plane — a cube's slice seen at an angle
    /// in the volume view — a drag of an inch covers far more data along the receding axis than across
    /// it, so an unprojected drag produces a mark much larger than the one you dragged out. The preview
    /// shows what you dragged; the mark has to BE what you dragged.
    /// </summary>
    public static double HalfFromDrag(IAnnotationSurface surface, AnnotationAnchor anchor, double screenPixels)
    {
        var scale = surface.UnitsToPixels(anchor);
        return double.IsFinite(scale) && scale > 0 ? screenPixels / scale : 0;
    }

    /// <summary>
    /// The half-size a resize drag is asking for, in the anchor's own units.
    ///
    /// <para>The grip is a corner, so the half-size is the LARGER of the two offsets — dragging away
    /// from the centre grows the shape whichever way you go, rather than only along the axis you
    /// happened to move furthest on.</para>
    ///
    /// <para>The drag is measured and floored entirely in SCREEN pixels, and converted once at the end
    /// through <see cref="HalfFromDrag"/>. Flooring after the conversion is what produced a minimum of
    /// half a degree on a sky-anchored mark — see <see cref="MinimumHalfPixels"/>. Measuring on screen
    /// is also what <see cref="HalfFromDrag"/> exists to do, so the drag that CREATES a mark and the
    /// drag that resizes it now go through one conversion rather than two that can disagree.</para>
    /// </summary>
    public static double? ResizeHalf(Annotation mark, IAnnotationSurface surface, double sx, double sy)
    {
        if (surface.Project(mark.Anchor) is not { } centre) return null;

        var scale = surface.UnitsToPixels(mark.Anchor);
        if (!double.IsFinite(scale) || scale <= 0) return null;

        var dragged = Math.Max(Math.Abs(sx - centre.X), Math.Abs(sy - centre.Y));
        if (!double.IsFinite(dragged)) return null;

        return HalfFromDrag(surface, mark.Anchor, Math.Max(dragged, MinimumHalfPixels));
    }
}
