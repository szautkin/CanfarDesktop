using System.Globalization;
using System.Text.Json.Serialization;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Models.Cutouts;

/// <summary>A direction on the sky, ICRS degrees.</summary>
public readonly record struct SkyPoint(double Ra, double Dec);

/// <summary>The shapes a cutout region can take.</summary>
public enum SkyShape
{
    /// <summary>Everything within a radius of a point — SODA's CIRCLE.</summary>
    Circle,

    /// <summary>A rectangle on the sky about a point, width along RA and height along Dec — sent as a POLYGON.</summary>
    Box,

    /// <summary>Any polygon — SODA's POLYGON.</summary>
    Polygon,
}

/// <summary>What is wrong with a region, when something is.</summary>
public enum RegionProblem
{
    NotANumber,
    DecOutOfRange,
    SizeNotPositive,
    TooLarge,
    TooFewVertices,
    Degenerate,
}

/// <summary>
/// A region on the sky: the one form a cutout region takes everywhere — the editor's fields and its
/// drawing, an agent's request, the saved Research record, and what goes to SODA.
///
/// <para>A box is its own shape rather than a polygon that happens to have four corners, so an editor
/// reopened on a saved cutout gets its width and height back rather than four vertices to guess from.
/// SODA has no box; it goes as the polygon of its corners.</para>
/// </summary>
public sealed record SkyRegion
{
    /// <summary>Anything bigger is not a cutout: SODA's circle stops at 180°, and no image is near 90°.</summary>
    public const double MaxSize = 90;

    public SkyShape Shape { get; init; }

    /// <summary>Centre, for a circle or a box.</summary>
    public double Ra { get; init; }

    /// <summary>Centre, for a circle or a box.</summary>
    public double Dec { get; init; }

    /// <summary>Degrees, for a circle.</summary>
    public double Radius { get; init; }

    /// <summary>Degrees on the sky along RA, for a box.</summary>
    public double Width { get; init; }

    /// <summary>Degrees along Dec, for a box.</summary>
    public double Height { get; init; }

    /// <summary>In order, for a polygon.</summary>
    public IReadOnlyList<SkyPoint> Vertices { get; init; } = [];

    public static SkyRegion Circle(double ra, double dec, double radius)
        => new() { Shape = SkyShape.Circle, Ra = SkyGeometry.NormaliseRa(ra), Dec = dec, Radius = radius };

    public static SkyRegion Box(double ra, double dec, double width, double height)
        => new() { Shape = SkyShape.Box, Ra = SkyGeometry.NormaliseRa(ra), Dec = dec, Width = width, Height = height };

    /// <summary>Wound the way CADC winds its own footprints, whichever way it was drawn.</summary>
    public static SkyRegion Polygon(IEnumerable<SkyPoint> vertices)
        => new()
        {
            Shape = SkyShape.Polygon,
            Vertices = SkyGeometry.SkyWise(vertices.Select(v => v with { Ra = SkyGeometry.NormaliseRa(v.Ra) }).ToList()),
        };

    /// <summary>The centre: given for a circle or box, the mean direction of a polygon's vertices.</summary>
    [JsonIgnore]
    public SkyPoint Centre => Shape == SkyShape.Polygon ? SkyGeometry.Centroid(Vertices) : new SkyPoint(Ra, Dec);

    /// <summary>
    /// The region as a polygon: a box's corners, a polygon's vertices, a circle as 64 points on its
    /// edge — what drawing and overlap work from.
    /// </summary>
    public IReadOnlyList<SkyPoint> Outline(int circleSegments = 64) => Shape switch
    {
        SkyShape.Circle => SkyGeometry.CircleOutline(Centre, Radius, circleSegments),
        SkyShape.Box => BoxCorners(),
        _ => Vertices,
    };

    /// <summary>How far the region reaches from its centre, degrees.</summary>
    [JsonIgnore]
    public double Reach => Shape == SkyShape.Circle
        ? Radius
        : Outline().Select(v => SkyGeometry.Distance(Centre, v)).DefaultIfEmpty(0).Max();

    /// <summary>The SODA parameter it is sent as.</summary>
    [JsonIgnore]
    public string SodaParameter => Shape == SkyShape.Circle ? "CIRCLE" : "POLYGON";

    /// <summary>The parameter's value: "ra dec radius", or "ra1 dec1 ra2 dec2 …", degrees.</summary>
    public string ToSoda() => Shape == SkyShape.Circle
        ? Join(Ra, Dec, Radius)
        : Join(Outline().SelectMany(v => new[] { v.Ra, v.Dec }).ToArray());

    /// <summary>Null when the region can be sent; otherwise what is wrong with it.</summary>
    public RegionProblem? Problem()
    {
        if (Shape == SkyShape.Polygon)
        {
            if (Vertices.Count < 3) return RegionProblem.TooFewVertices;
            if (Vertices.Any(v => !double.IsFinite(v.Ra) || !double.IsFinite(v.Dec))) return RegionProblem.NotANumber;
            if (Vertices.Any(v => v.Dec is < -90 or > 90)) return RegionProblem.DecOutOfRange;
            if (SkyGeometry.Area(Vertices) <= 1e-12) return RegionProblem.Degenerate;
            return Reach > MaxSize ? RegionProblem.TooLarge : null;
        }

        var sizes = Shape == SkyShape.Circle ? new[] { Radius } : new[] { Width, Height };
        if (!double.IsFinite(Ra) || !double.IsFinite(Dec) || sizes.Any(s => !double.IsFinite(s)))
            return RegionProblem.NotANumber;
        if (Dec is < -90 or > 90) return RegionProblem.DecOutOfRange;
        if (sizes.Any(s => s <= 0)) return RegionProblem.SizeNotPositive;
        return sizes.Any(s => s > MaxSize) ? RegionProblem.TooLarge : null;
    }

    /// <summary>
    /// A short account of it — "r 2.0′ @ 10.68000°, +41.27000°", "5.0′ × 3.0′ @ …". Symbols rather
    /// than words, so it reads the same in either language.
    /// </summary>
    public string Describe()
    {
        var c = Centre;
        var at = $"@ {c.Ra.ToString("0.00000", CultureInfo.CurrentCulture)}°, {c.Dec.ToString("+0.00000;-0.00000", CultureInfo.CurrentCulture)}°";
        return Shape switch
        {
            SkyShape.Circle => $"r {Angle(Radius)} {at}",
            SkyShape.Box => $"{Angle(Width)} × {Angle(Height)} {at}",
            _ => $"⬠ {Vertices.Count} {at}",
        };
    }

    /// <summary>An angle at the unit that reads best: arcseconds, arcminutes, or degrees.</summary>
    public static string Angle(double degrees)
    {
        var culture = CultureInfo.CurrentCulture;
        return degrees switch
        {
            < 1.0 / 60 => $"{(degrees * 3600).ToString("0.#", culture)}″",
            < 1 => $"{(degrees * 60).ToString("0.##", culture)}′",
            _ => $"{degrees.ToString("0.###", culture)}°",
        };
    }

    /// <summary>
    /// Corners in the order CADC writes a footprint — south-east, south-west, north-west, north-east —
    /// placed through the tangent plane so the box is a true rectangle on the sky at any Dec.
    /// </summary>
    private IReadOnlyList<SkyPoint> BoxCorners()
    {
        var centre = new SkyPoint(Ra, Dec);
        var (hw, hh) = (Width / 2, Height / 2);
        return
        [
            SkyGeometry.Unproject(centre, hw, -hh),
            SkyGeometry.Unproject(centre, -hw, -hh),
            SkyGeometry.Unproject(centre, -hw, hh),
            SkyGeometry.Unproject(centre, hw, hh),
        ];
    }

    private static string Join(params double[] values)
        => string.Join(' ', values.Select(v => v.ToString("0.##########", CultureInfo.InvariantCulture)));
}
