using System.Globalization;

namespace CanfarDesktop.Models;

/// <summary>The coordinate space a mark is pinned in.</summary>
public enum AnchorSpace
{
    /// <summary>FITS image pixels. Survives pan, zoom and rotation.</summary>
    ImagePixel,

    /// <summary>
    /// Sky position in degrees. Survives reopening the file, and points at the same place in a
    /// DIFFERENT image of the same field — which is why it is preferred whenever the FITS has WCS.
    /// </summary>
    Sky,

    /// <summary>Cube voxel space (x, y, channel).</summary>
    Data,
}

/// <summary>
/// Where a mark is pinned, in the viewer's own coordinates.
///
/// <b>Never screen pixels.</b> A mark pinned to the window slides off its subject the moment anyone
/// pans, and nobody notices until they zoom.
/// </summary>
public sealed record AnnotationAnchor(AnchorSpace Space, double X, double Y, double Z = 0)
{
    public static AnnotationAnchor ImagePixel(double x, double y) => new(AnchorSpace.ImagePixel, x, y);
    public static AnnotationAnchor Sky(double raDeg, double decDeg) => new(AnchorSpace.Sky, raDeg, decDeg);
    public static AnnotationAnchor Data(double x, double y, double z) => new(AnchorSpace.Data, x, y, z);

    /// <summary>
    /// Whether this anchor can be drawn at all.
    ///
    /// A NaN reaches the drawing layer, draws nothing, and reports no error — the mark is simply
    /// absent, which is indistinguishable from one that was never created. Sky coordinates are
    /// range-checked too: a Dec of 120° is not a place, and silently drawing it somewhere is worse
    /// than refusing it.
    /// </summary>
    public bool IsValid
    {
        get
        {
            if (!double.IsFinite(X) || !double.IsFinite(Y) || !double.IsFinite(Z)) return false;
            return Space != AnchorSpace.Sky || (X >= 0 && X < 360 && Y >= -90 && Y <= 90);
        }
    }

    /// <summary>The space name as it goes over the wire.</summary>
    public string SpaceName => Space switch
    {
        AnchorSpace.ImagePixel => "imagePixel",
        AnchorSpace.Sky => "sky",
        _ => "data",
    };

    public static AnchorSpace? ParseSpace(string? text) => (text ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "imagepixel" or "image" or "pixel" => AnchorSpace.ImagePixel,
        "sky" or "world" or "icrs" => AnchorSpace.Sky,
        "data" or "voxel" or "cube" => AnchorSpace.Data,
        _ => null,
    };
}

/// <summary>What a mark looks like.</summary>
public enum AnnotationKind
{
    /// <summary>A box around the subject.</summary>
    Rect,

    /// <summary>A circle around it — a sphere in a cube, so it projects to an ellipse off-axis.</summary>
    Circle,

    /// <summary>A shape with a leader line to a label set clear of the subject.</summary>
    Callout,

    /// <summary>A label alone, at the anchor.</summary>
    Text,
}

public static class AnnotationKindExtensions
{
    /// <summary>Whether this kind draws a shape that needs an extent.</summary>
    public static bool NeedsExtent(this AnnotationKind kind)
        => kind is AnnotationKind.Rect or AnnotationKind.Circle;

    public static string AsString(this AnnotationKind kind) => kind switch
    {
        AnnotationKind.Rect => "rect",
        AnnotationKind.Circle => "circle",
        AnnotationKind.Callout => "callout",
        _ => "text",
    };

    /// <summary>Parse a kind from a tool argument, accepting the words people actually use.</summary>
    public static AnnotationKind? Parse(string? text) => (text ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "rect" or "rectangle" or "box" or "square" => AnnotationKind.Rect,
        "circle" or "ellipse" => AnnotationKind.Circle,
        "callout" or "label" or "leader" => AnnotationKind.Callout,
        "text" or "note" => AnnotationKind.Text,
        _ => null,
    };
}

/// <summary>
/// How big a shape is, in the anchor's own units.
///
/// Data units, not screen pixels: a circle drawn around a source should stay around that source as the
/// view zooms, the way a circle drawn on a photograph does. A screen-sized shape looks right at one
/// zoom level and at no other.
/// </summary>
public sealed record Extent(double HalfWidth, double HalfHeight)
{
    public static Extent Square(double half) => new(half, half);

    /// <summary>A shape with no area cannot be seen or clicked.</summary>
    public bool IsValid => double.IsFinite(HalfWidth) && double.IsFinite(HalfHeight)
                        && HalfWidth > 0 && HalfHeight > 0;
}

/// <summary>
/// Who drew a mark. An agent drawing on someone's screen without saying so is how a feature like this
/// loses trust: the panel shows it, the payload reports it, and the style gives an agent's marks their
/// own accent.
/// </summary>
public enum MarkAuthor { User, Agent }

/// <summary>
/// How a mark is drawn: its ink, its label, and the weight of its outline.
///
/// Per-mark rather than global, because a mark persists with its file, travels over MCP, and ends up in
/// an exported figure that has to look the same when it is opened again.
///
/// Sizes are in DEVICE PIXELS and are not scaled by zoom, for the reason a hairline stroke always was:
/// a stroke that thickens as you zoom out turns the view into a blot. An export still scales them —
/// see <see cref="UsableInk"/>.
/// </summary>
public sealed record MarkStyle(double Red, double Green, double Blue, double FontSize, bool Bold, double Stroke)
{
    /// <summary>The drawing ink: cold white-cyan.</summary>
    public static readonly MarkStyle UserDefault = FromBytes(158, 217, 255, DefaultFontSize, false, DefaultStroke);

    /// <summary>An agent's marks, distinguishable without being louder.</summary>
    public static readonly MarkStyle AgentDefault = FromBytes(140, 255, 204, DefaultFontSize, false, DefaultStroke);

    public const double DefaultFontSize = 11.0;
    public const double DefaultStroke = 1.0;

    /// <summary>
    /// A style from 8-bit channels — because 8 bits is what a colour SURVIVES as. It is stored as
    /// #rrggbb, shown in a colour button as #rrggbb, and sent over MCP as #rrggbb; a constant with more
    /// precision than that cannot come back from its own storage, which is how a default ends up not
    /// equal to itself after one round trip.
    /// </summary>
    public static MarkStyle FromBytes(byte r, byte g, byte b, double fontSize, bool bold, double stroke)
        => new(r / 255.0, g / 255.0, b / 255.0, fontSize, bold, stroke);

    /// <summary>What a mark by <paramref name="author"/> looks like when nothing has been said about it.</summary>
    public static MarkStyle ForAuthor(MarkAuthor author)
        => author == MarkAuthor.Agent ? AgentDefault : UserDefault;

    /// <summary>The colour as <c>#rrggbb</c>.</summary>
    public string ColourHex()
    {
        static int Q(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255);
        return string.Create(CultureInfo.InvariantCulture, $"#{Q(Red):x2}{Q(Green):x2}{Q(Blue):x2}");
    }

    /// <summary>
    /// Parse <c>#rrggbb</c> or <c>rrggbb</c>, case-insensitively. Null for anything else, so a caller
    /// can say what was wrong rather than a typo silently producing black — which on a dark image is a
    /// mark that has vanished.
    /// </summary>
    public static (double R, double G, double B)? ColourFromHex(string? text)
    {
        var t = (text ?? string.Empty).Trim().TrimStart('#');
        if (t.Length != 6 || !t.All(Uri.IsHexDigit)) return null;

        static double Channel(string s, int at) => Convert.ToInt32(s.Substring(at, 2), 16) / 255.0;
        return (Channel(t, 0), Channel(t, 2), Channel(t, 4));
    }

    public MarkStyle WithColourHex(string hex)
        => ColourFromHex(hex) is { } c ? this with { Red = c.R, Green = c.G, Blue = c.B } : this;

    /// <summary>
    /// Clamped to what can actually be drawn and read. A zero stroke draws nothing and a zero font size
    /// is an invisible label — both look like the mark having been lost. The ceilings stop one mark from
    /// covering the frame.
    /// </summary>
    public MarkStyle Sane()
    {
        static double Channel(double v) => double.IsFinite(v) ? Math.Clamp(v, 0, 1) : 0;
        return this with
        {
            Red = Channel(Red),
            Green = Channel(Green),
            Blue = Channel(Blue),
            FontSize = double.IsFinite(FontSize) ? Math.Clamp(FontSize, 6, 72) : DefaultFontSize,
            Stroke = double.IsFinite(Stroke) ? Math.Clamp(Stroke, 0.5, 20) : DefaultStroke,
        };
    }

    /// <summary>
    /// The style as one short string, for the settings store: <c>#rrggbb|size|bold|stroke</c>.
    ///
    /// One value rather than four, because a style is only meaningful whole. Four separate settings can
    /// be half-written — the colour saved and the stroke not — and a half-applied style is a state
    /// nothing else in the app can produce and nothing knows how to repair.
    /// </summary>
    public string Encode()
        => string.Create(CultureInfo.InvariantCulture,
            $"{ColourHex()}|{FontSize:0.##}|{(Bold ? 1 : 0)}|{Stroke:0.##}");

    /// <summary>
    /// Read back what <see cref="Encode"/> wrote, or <paramref name="fallback"/> for anything else.
    ///
    /// Anything else includes a value from a future build with more fields in it. A style that cannot be
    /// read is not an error worth showing anybody: the marks still draw, in the colour they would have
    /// had before this setting existed.
    /// </summary>
    public static MarkStyle Decode(string? text, MarkStyle fallback)
    {
        var parts = (text ?? string.Empty).Split('|');
        if (parts.Length != 4) return fallback;
        if (ColourFromHex(parts[0]) is not { } colour) return fallback;

        var n = NumberStyles.Float;
        var c = CultureInfo.InvariantCulture;
        if (!double.TryParse(parts[1], n, c, out var fontSize)) return fallback;
        if (!double.TryParse(parts[3], n, c, out var stroke)) return fallback;

        return new MarkStyle(colour.R, colour.G, colour.B, fontSize, parts[2] == "1", stroke).Sane();
    }

    /// <summary>
    /// An ink factor that can actually be drawn with.
    ///
    /// One definition, because the factor is applied in several places — the stroke, the font, the
    /// leader, the rule — and a zero caught in one of them and not the others draws a mark with a
    /// full-size ring and no leader at all. An ink scale is derived from a surface's size, and a
    /// headless one is zero: a probe, or an agent asking before the window is shown.
    /// </summary>
    public static double UsableInk(double ink) => double.IsFinite(ink) && ink > 0 ? ink : 1.0;
}

/// <summary>
/// A mark on a viewer.
///
/// One model for both viewers. The FITS canvas is flat and the cube's volume is not, and the only thing
/// that differs between them is how a point becomes a pixel — so the anchor carries the coordinates and
/// the viewer supplies the projection. Everything else, from the callout geometry to the label, is the
/// same code in both places.
/// </summary>
public sealed record Annotation
{
    /// <summary>Stable id, unique within one target.</summary>
    public string Id { get; init; } = string.Empty;

    public AnnotationKind Kind { get; init; } = AnnotationKind.Circle;

    public AnnotationAnchor Anchor { get; init; } = AnnotationAnchor.ImagePixel(0, 0);

    /// <summary>Size, for the kinds that have one.</summary>
    public Extent? Extent { get; init; }

    /// <summary>The label. May be empty for a bare shape.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Where a callout's label sits, in SCREEN pixels from the anchor.
    ///
    /// Screen and not data units, deliberately, and the one place that is right: a label is furniture,
    /// not part of the image. In a cube it would otherwise shear and shrink as the camera moved, and
    /// the text would stop being readable — which is the one thing a label has to be.
    /// </summary>
    public double? LabelOffsetX { get; init; }
    public double? LabelOffsetY { get; init; }

    public MarkAuthor Author { get; init; } = MarkAuthor.User;

    /// <summary>
    /// How this mark is drawn, when it has been said explicitly.
    ///
    /// Null means "however a mark by this author is drawn" — which is what every mark stored before
    /// styling existed means, and what it has always meant. That is why it is nullable rather than a
    /// value with defaults: a defaulted style would give every stored agent mark the USER colour on
    /// load, silently restyling work people had already done, with no error to notice.
    /// </summary>
    public MarkStyle? Style { get; init; }

    /// <summary>RFC-3339, for the panel's ordering.</summary>
    public string CreatedAt { get; init; } = string.Empty;

    /// <summary>The style to draw this mark with — its own, or its author's.</summary>
    public MarkStyle EffectiveStyle => (Style ?? MarkStyle.ForAuthor(Author)).Sane();

    /// <summary>What is wrong with this mark, or null when it can be drawn.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
            return "an annotation needs an id";

        if (!Anchor.IsValid)
            return $"the {Anchor.SpaceName} position is not a place that can be drawn (not finite, or off the sky)";

        if (Extent is not null)
        {
            if (!Extent.IsValid) return "a shape needs a width and height greater than zero";
        }
        else if (Kind.NeedsExtent())
        {
            return $"a {Kind.AsString()} needs a size — give it a radius or a width and height";
        }

        if ((LabelOffsetX is { } dx && !double.IsFinite(dx)) || (LabelOffsetY is { } dy && !double.IsFinite(dy)))
            return "the label offset is not a finite distance";

        // A callout with nothing to say is a leader line pointing at a blank rule — it looks like a
        // rendering fault rather than a mark.
        if (Kind == AnnotationKind.Callout && string.IsNullOrWhiteSpace(Text))
            return "a callout needs text — its whole purpose is the label";

        if (Kind == AnnotationKind.Text && string.IsNullOrWhiteSpace(Text))
            return "a text mark needs text";

        return null;
    }
}
