using CanfarDesktop.Helpers;
using CanfarDesktop.Models.Cutouts;
using CanfarDesktop.Models.Fits;

namespace CanfarDesktop.Services.Cutouts.Local;

/// <summary>A rectangle of whole pixels, in FITS's numbering: 1-based, both ends included.</summary>
public readonly record struct PixelBox(int X0, int Y0, int X1, int Y1)
{
    public int Width => X1 - X0 + 1;
    public int Height => Y1 - Y0 + 1;

    /// <summary>"[101:300,51:250]" — how IRAF and every FITS tool write a section.</summary>
    public override string ToString() => $"[{X0}:{X1},{Y0}:{Y1}]";

    /// <summary>
    /// The pixels a region on the sky covers on an image: the smallest box of whole pixels holding
    /// it, cut to the image — the same box SODA cuts, so a circle comes back as its square. Null when
    /// the region misses the image, or the image's coordinates cannot place it at all.
    ///
    /// <para>The region's outline is followed closely — a point every few degrees of a circle, sixteen
    /// along each side of a box — because through a distorted WCS (SIP) a side is not a straight line
    /// in pixels, and its corners alone would miss a bulge.</para>
    /// </summary>
    public static PixelBox? Around(SkyRegion region, WcsInfo wcs, int width, int height)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        var placed = 0;
        foreach (var point in Densify(region))
        {
            if (wcs.WorldToPixel(point.Ra, point.Dec) is not { } p || !double.IsFinite(p.Px) || !double.IsFinite(p.Py))
                continue;
            minX = Math.Min(minX, p.Px);
            maxX = Math.Max(maxX, p.Px);
            minY = Math.Min(minY, p.Py);
            maxY = Math.Max(maxY, p.Py);
            placed++;
        }
        if (placed == 0) return null;

        // Pixel n covers n − ½ up to n + ½.
        var x0 = PixelOf(minX);
        var x1 = PixelOf(maxX);
        var y0 = PixelOf(minY);
        var y1 = PixelOf(maxY);
        if (x1 < 1 || y1 < 1 || x0 > width || y0 > height) return null;

        return new PixelBox(Math.Max(1, x0), Math.Max(1, y0), Math.Min(width, x1), Math.Min(height, y1));
    }

    private static int PixelOf(double coordinate)
        => (int)Math.Clamp(Math.Floor(coordinate + 0.5), int.MinValue / 2, int.MaxValue / 2);

    /// <summary>The outline with points added along each side, through the tangent plane about its centre.</summary>
    private static IEnumerable<SkyPoint> Densify(SkyRegion region)
    {
        var outline = region.Outline();
        var perSide = Math.Max(1, 64 / Math.Max(1, outline.Count));
        var centre = region.Centre;
        for (var i = 0; i < outline.Count; i++)
        {
            if (SkyGeometry.Project(centre, outline[i]) is not { } a
                || SkyGeometry.Project(centre, outline[(i + 1) % outline.Count]) is not { } b)
            {
                yield return outline[i];
                continue;
            }
            for (var k = 0; k < perSide; k++)
            {
                var t = (double)k / perSide;
                yield return SkyGeometry.Unproject(centre, a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y));
            }
        }
    }
}
