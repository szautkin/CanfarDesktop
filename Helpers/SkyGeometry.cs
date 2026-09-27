using CanfarDesktop.Models.Cutouts;

namespace CanfarDesktop.Helpers;

/// <summary>How a region sits against a footprint.</summary>
public enum SkyOverlap
{
    /// <summary>Wholly inside it.</summary>
    Inside,

    /// <summary>Partly inside: a cutout would be trimmed to the footprint.</summary>
    Partial,

    /// <summary>Not touching it at all.</summary>
    Outside,
}

/// <summary>
/// Geometry on the celestial sphere, in degrees, for regions the size of an image — the arithmetic a
/// cutout needs and nothing a catalogue would.
///
/// <para>Shapes are compared in the gnomonic (tangent-plane) projection about the footprint's centre,
/// where great circles are straight lines: exact for containment of polygon vertices and edges, and
/// true to well under a pixel across a field of a degree or two. Working in raw RA and Dec instead —
/// which the observation view's footprint sketch did — squashes every field by cos(Dec) and breaks
/// outright across RA 0°/360°.</para>
///
/// <para>The plane's axes are ξ toward increasing RA (east) and η toward north, in degrees.</para>
/// </summary>
public static class SkyGeometry
{
    private const double Rad = Math.PI / 180;

    /// <summary>RA folded into [0, 360).</summary>
    public static double NormaliseRa(double ra)
    {
        var r = ra % 360;
        return r < 0 ? r + 360 : r;
    }

    /// <summary>The angle between two points, in degrees.</summary>
    public static double Distance(SkyPoint a, SkyPoint b)
    {
        var dRa = (b.Ra - a.Ra) * Rad;
        var dDec = (b.Dec - a.Dec) * Rad;
        var h = Math.Sin(dDec / 2) * Math.Sin(dDec / 2)
              + Math.Cos(a.Dec * Rad) * Math.Cos(b.Dec * Rad) * Math.Sin(dRa / 2) * Math.Sin(dRa / 2);
        return 2 * Math.Asin(Math.Min(1, Math.Sqrt(h))) / Rad;
    }

    /// <summary>The centre of a set of points: the mean direction, so it is right across RA 0°.</summary>
    public static SkyPoint Centroid(IEnumerable<SkyPoint> points)
    {
        double x = 0, y = 0, z = 0;
        var any = false;
        foreach (var p in points)
        {
            var (px, py, pz) = UnitVector(p);
            x += px; y += py; z += pz;
            any = true;
        }
        if (!any) return new SkyPoint(0, 0);

        var ra = NormaliseRa(Math.Atan2(y, x) / Rad);
        var dec = Math.Atan2(z, Math.Sqrt(x * x + y * y)) / Rad;
        return new SkyPoint(ra, dec);
    }

    /// <summary>
    /// A point's place in the tangent plane about <paramref name="centre"/>, in degrees — or null for
    /// a point 90° or more away, which the projection cannot show.
    /// </summary>
    public static (double X, double Y)? Project(SkyPoint centre, SkyPoint p)
    {
        var d0 = centre.Dec * Rad;
        var d = p.Dec * Rad;
        var da = (p.Ra - centre.Ra) * Rad;
        var cosC = Math.Sin(d0) * Math.Sin(d) + Math.Cos(d0) * Math.Cos(d) * Math.Cos(da);
        if (cosC <= 1e-12) return null;

        var xi = Math.Cos(d) * Math.Sin(da) / cosC;
        var eta = (Math.Cos(d0) * Math.Sin(d) - Math.Sin(d0) * Math.Cos(d) * Math.Cos(da)) / cosC;
        return (xi / Rad, eta / Rad);
    }

    /// <summary>The point at (ξ east, η north) degrees in the tangent plane about <paramref name="centre"/>.</summary>
    public static SkyPoint Unproject(SkyPoint centre, double x, double y)
    {
        var xi = x * Rad;
        var eta = y * Rad;
        var rho = Math.Sqrt(xi * xi + eta * eta);
        if (rho < 1e-15) return centre;

        var c = Math.Atan(rho);
        var d0 = centre.Dec * Rad;
        var dec = Math.Asin(Math.Cos(c) * Math.Sin(d0) + eta * Math.Sin(c) * Math.Cos(d0) / rho);
        var ra = centre.Ra * Rad + Math.Atan2(xi * Math.Sin(c),
            rho * Math.Cos(d0) * Math.Cos(c) - eta * Math.Sin(d0) * Math.Sin(c));
        return new SkyPoint(NormaliseRa(ra / Rad), dec / Rad);
    }

    /// <summary>A circle's outline: <paramref name="segments"/> points exactly <paramref name="radius"/> degrees from its centre.</summary>
    public static IReadOnlyList<SkyPoint> CircleOutline(SkyPoint centre, double radius, int segments = 64)
    {
        var d1 = centre.Dec * Rad;
        var delta = radius * Rad;
        var points = new List<SkyPoint>(segments);
        for (var i = 0; i < segments; i++)
        {
            // Bearing from north through east, so the outline runs counter-clockwise on the sky.
            var theta = 2 * Math.PI * i / segments;
            var d2 = Math.Asin(Math.Sin(d1) * Math.Cos(delta) + Math.Cos(d1) * Math.Sin(delta) * Math.Cos(theta));
            var a2 = centre.Ra * Rad + Math.Atan2(Math.Sin(theta) * Math.Sin(delta) * Math.Cos(d1),
                Math.Cos(delta) - Math.Sin(d1) * Math.Sin(d2));
            points.Add(new SkyPoint(NormaliseRa(a2 / Rad), d2 / Rad));
        }
        return points;
    }

    /// <summary>
    /// A polygon's signed area in the tangent plane about its centroid, square degrees. NEGATIVE for
    /// the winding CADC writes its own footprints in — counter-clockwise as the sky is seen, east to
    /// the left — because the plane's ξ axis runs east to the right.
    /// </summary>
    public static double SignedArea(IReadOnlyList<SkyPoint> polygon)
    {
        if (polygon.Count < 3) return 0;
        var centre = Centroid(polygon);
        var plane = Plane(centre, polygon);
        if (plane is null) return 0;

        double sum = 0;
        for (var i = 0; i < plane.Count; i++)
        {
            var (x1, y1) = plane[i];
            var (x2, y2) = plane[(i + 1) % plane.Count];
            sum += x1 * y2 - x2 * y1;
        }
        return sum / 2;
    }

    /// <summary>The same polygon wound the way CADC winds its footprints.</summary>
    public static IReadOnlyList<SkyPoint> SkyWise(IReadOnlyList<SkyPoint> polygon)
        => SignedArea(polygon) > 0 ? polygon.Reverse().ToList() : polygon;

    /// <summary>Whether a point falls inside a polygon.</summary>
    public static bool Contains(IReadOnlyList<SkyPoint> polygon, SkyPoint p)
    {
        if (polygon.Count < 3) return false;
        var centre = Centroid(polygon);
        var plane = Plane(centre, polygon);
        return plane is not null && Project(centre, p) is { } q && Inside(plane, q);
    }

    /// <summary>
    /// How <paramref name="region"/> (an outline) sits against <paramref name="footprint"/>.
    ///
    /// <para>Inside when every point of the outline is; otherwise Partial if the two touch at all —
    /// an outline point inside the footprint, a footprint corner inside the outline, or crossing edges
    /// (a thin region straddling a corner has none of the first two); otherwise Outside.</para>
    /// </summary>
    public static SkyOverlap Overlap(IReadOnlyList<SkyPoint> region, IReadOnlyList<SkyPoint> footprint)
    {
        if (region.Count < 3 || footprint.Count < 3) return SkyOverlap.Outside;

        var centre = Centroid(footprint);
        var fp = Plane(centre, footprint);
        var rg = Plane(centre, region);
        if (fp is null || rg is null) return SkyOverlap.Outside;

        var inside = rg.Count(p => Inside(fp, p));
        if (inside == rg.Count) return SkyOverlap.Inside;
        if (inside > 0 || fp.Any(p => Inside(rg, p)) || EdgesCross(rg, fp)) return SkyOverlap.Partial;
        return SkyOverlap.Outside;
    }

    /// <summary>
    /// The smallest convex polygon holding every point, wound as CADC winds its footprints — the one
    /// outline of a mosaic's CCDs, or an HST image's chips. Built in the tangent plane about the
    /// points' centre (Andrew's monotone chain), where the great-circle edges are straight lines.
    /// </summary>
    public static IReadOnlyList<SkyPoint> ConvexHull(IReadOnlyList<SkyPoint> points)
    {
        // Square degrees: a point 1e-13° off a side one arcsecond long is on it.
        const double Collinear = 1e-15;
        if (points.Count < 3) return points;
        var centre = Centroid(points);
        var plane = Plane(centre, points);
        if (plane is null) return points;

        var sorted = plane.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        if (sorted.Count < 3) return points;

        static double Turn((double X, double Y) o, (double X, double Y) a, (double X, double Y) b)
            => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

        var hull = new List<(double X, double Y)>();
        foreach (var pass in new[] { sorted, Enumerable.Reverse(sorted).ToList() })
        {
            var start = hull.Count;
            foreach (var p in pass)
            {
                // Points on a side, to within rounding, are not corners of it.
                while (hull.Count >= start + 2 && Turn(hull[^2], hull[^1], p) <= Collinear) hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            hull.RemoveAt(hull.Count - 1); // each pass ends where the other begins
        }

        return SkyWise(hull.Select(p => Unproject(centre, p.X, p.Y)).ToList());
    }

    /// <summary>A polygon's area, square degrees — for estimating what share of a file a cutout is.</summary>
    public static double Area(IReadOnlyList<SkyPoint> polygon) => Math.Abs(SignedArea(polygon));

    private static (double X, double Y, double Z) UnitVector(SkyPoint p)
    {
        var ra = p.Ra * Rad;
        var dec = p.Dec * Rad;
        return (Math.Cos(dec) * Math.Cos(ra), Math.Cos(dec) * Math.Sin(ra), Math.Sin(dec));
    }

    private static List<(double X, double Y)>? Plane(SkyPoint centre, IReadOnlyList<SkyPoint> points)
    {
        var plane = new List<(double X, double Y)>(points.Count);
        foreach (var p in points)
        {
            if (Project(centre, p) is not { } q) return null;
            plane.Add(q);
        }
        return plane;
    }

    /// <summary>Ray casting; a point exactly on an edge may land either way, which a cutout does not mind.</summary>
    private static bool Inside(IReadOnlyList<(double X, double Y)> polygon, (double X, double Y) p)
    {
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var (xi, yi) = polygon[i];
            var (xj, yj) = polygon[j];
            if ((yi > p.Y) != (yj > p.Y) && p.X < (xj - xi) * (p.Y - yi) / (yj - yi) + xi)
                inside = !inside;
        }
        return inside;
    }

    private static bool EdgesCross(IReadOnlyList<(double X, double Y)> a, IReadOnlyList<(double X, double Y)> b)
    {
        for (var i = 0; i < a.Count; i++)
        {
            var a1 = a[i];
            var a2 = a[(i + 1) % a.Count];
            for (var j = 0; j < b.Count; j++)
                if (SegmentsCross(a1, a2, b[j], b[(j + 1) % b.Count])) return true;
        }
        return false;
    }

    private static bool SegmentsCross((double X, double Y) p1, (double X, double Y) p2,
                                      (double X, double Y) q1, (double X, double Y) q2)
    {
        static double Turn((double X, double Y) a, (double X, double Y) b, (double X, double Y) c)
            => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

        var d1 = Turn(q1, q2, p1);
        var d2 = Turn(q1, q2, p2);
        var d3 = Turn(p1, p2, q1);
        var d4 = Turn(p1, p2, q2);
        return ((d1 > 0) != (d2 > 0)) && ((d3 > 0) != (d4 > 0));
    }
}
