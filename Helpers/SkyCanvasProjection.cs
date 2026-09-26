using CanfarDesktop.Models.Cutouts;

namespace CanfarDesktop.Helpers;

/// <summary>
/// A patch of sky laid out on a canvas the way a sky chart is: north up, east to the LEFT, one scale
/// for both axes — and back again, for a pointer on the canvas.
///
/// <para>Through the tangent plane (<see cref="SkyGeometry"/>), so a footprint keeps its true shape at
/// any Dec and across RA 0°. The observation view's sketch used to scale RA and Dec separately, which
/// stretched every field to fill its box and squashed nothing by cos(Dec); one projection now serves
/// that sketch and the cutout editor both.</para>
/// </summary>
public sealed class SkyCanvasProjection
{
    private readonly double _scale;   // canvas pixels per degree
    private readonly double _cx, _cy; // where the centre lands
    private readonly double _ox, _oy; // plane offset of the fitted box's middle

    /// <summary>The direction the projection is about.</summary>
    public SkyPoint Centre { get; }

    public double Width { get; }
    public double Height { get; }

    /// <param name="fit">Everything that must be visible; the canvas is scaled to hold it all.</param>
    /// <param name="padding">Share of each side left clear, so nothing touches the edge.</param>
    public SkyCanvasProjection(IEnumerable<SkyPoint> fit, double width, double height, double padding = 0.1)
    {
        var points = fit.ToList();
        Width = width;
        Height = height;
        Centre = SkyGeometry.Centroid(points);

        var plane = points.Select(p => SkyGeometry.Project(Centre, p)).OfType<(double X, double Y)>().ToList();
        double minX = 0, maxX = 0, minY = 0, maxY = 0;
        if (plane.Count > 0)
        {
            minX = plane.Min(p => p.X); maxX = plane.Max(p => p.X);
            minY = plane.Min(p => p.Y); maxY = plane.Max(p => p.Y);
        }

        var spanX = Math.Max(maxX - minX, 1e-9);
        var spanY = Math.Max(maxY - minY, 1e-9);
        var usable = 1 - 2 * padding;
        _scale = Math.Min(width * usable / spanX, height * usable / spanY);
        _ox = (minX + maxX) / 2;
        _oy = (minY + maxY) / 2;
        _cx = width / 2;
        _cy = height / 2;
    }

    /// <summary>How many degrees one canvas pixel spans.</summary>
    public double DegreesPerPixel => 1 / _scale;

    /// <summary>Where a direction lands on the canvas; null if it is too far round the sky to show.</summary>
    public (double X, double Y)? ToCanvas(SkyPoint p)
        => SkyGeometry.Project(Centre, p) is { } q
            ? (_cx - (q.X - _ox) * _scale, _cy - (q.Y - _oy) * _scale) // east left, north up
            : null;

    /// <summary>The direction under a canvas point.</summary>
    public SkyPoint ToSky(double x, double y)
        => SkyGeometry.Unproject(Centre, (_cx - x) / _scale + _ox, (_cy - y) / _scale + _oy);
}
