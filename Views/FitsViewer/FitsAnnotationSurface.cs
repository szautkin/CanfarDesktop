using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Models.Fits;

namespace CanfarDesktop.Views.FitsViewer;

/// <summary>
/// The FITS canvas, as the annotation renderer sees it: three questions, answered through the same
/// transform the crosshair uses.
///
/// It takes functions rather than the page so that the projection can be tested — the arithmetic here
/// is where a mark ends up in the wrong place, and that is not something to find out by looking.
/// </summary>
public sealed class FitsAnnotationSurface : IAnnotationSurface
{
    private readonly Func<double, double, (double X, double Y)> _imageToScreen;
    private readonly Func<WcsInfo?> _wcs;

    public FitsAnnotationSurface(Func<double, double, (double X, double Y)> imageToScreen, Func<WcsInfo?> wcs)
    {
        _imageToScreen = imageToScreen;
        _wcs = wcs;
    }

    /// <summary>1.0 on screen. An export plate constructs its own surface with its own factor.</summary>
    public double InkScale { get; init; } = 1.0;

    public (double X, double Y)? Project(AnnotationAnchor anchor)
    {
        if (!anchor.IsValid) return null;

        return anchor.Space switch
        {
            AnchorSpace.ImagePixel => _imageToScreen(anchor.X, anchor.Y),
            AnchorSpace.Sky => ProjectSky(anchor.X, anchor.Y),

            // A cube mark on a FITS canvas. Not an error and not clamped: it belongs to another
            // viewer's space, and a clamped mark points at the wrong thing.
            _ => null,
        };
    }

    private (double X, double Y)? ProjectSky(double raDeg, double decDeg)
    {
        if (_wcs() is not { } wcs) return null;
        if (wcs.WorldToPixel(raDeg, decDeg) is not { } pixel) return null;

        return _imageToScreen(pixel.Px, pixel.Py);
    }

    /// <summary>
    /// How many screen pixels one unit spans, measured rather than derived: project the anchor and a
    /// point one unit away and take the distance between them.
    ///
    /// Measuring keeps this correct through zoom, rotation, a parity flip and the display scaling of
    /// the image, without this file needing to know that any of them exist. A scale computed from the
    /// zoom alone was right until the day north-up rotation landed.
    /// </summary>
    public double UnitsToPixels(AnnotationAnchor anchor)
    {
        if (Project(anchor) is not { } here) return 1.0;

        var stepped = anchor.Space switch
        {
            AnchorSpace.ImagePixel => AnnotationAnchor.ImagePixel(anchor.X + 1, anchor.Y),

            // A degree of RA is not a degree on the sky except at the equator, so the step is taken in
            // Dec — where one degree is one degree everywhere, and a circle drawn with it is the size
            // it says it is.
            AnchorSpace.Sky => AnnotationAnchor.Sky(anchor.X, Math.Clamp(anchor.Y + SkyStep, -90, 90)),
            _ => null,
        };

        if (stepped is null || Project(stepped) is not { } there) return 1.0;

        var span = Math.Sqrt(Math.Pow(there.X - here.X, 2) + Math.Pow(there.Y - here.Y, 2));
        var perUnit = anchor.Space == AnchorSpace.Sky ? span / SkyStep : span;

        // A degenerate scale draws a mark with no size, which looks like a mark that was lost.
        return double.IsFinite(perUnit) && perUnit > 0 ? perUnit : 1.0;
    }

    /// <summary>
    /// Small enough to be locally linear, large enough not to be lost to floating point in the
    /// projection. A tenth of an arcsecond is neither.
    /// </summary>
    private const double SkyStep = 0.001;
}
