using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Models.Fits;

namespace CanfarDesktop.Views.FitsViewer;

/// <summary>
/// The exported figure, as the annotation renderer sees it.
///
/// A plate is not the canvas: it shows one REGION of the image, at whatever size the figure is, with no
/// zoom, no pan and no rotation. So the mapping is a plain scale and offset — which is the whole reason
/// the marks can be drawn on it by the same code that draws them on screen. It answers the same three
/// questions; only the arithmetic behind them differs.
///
/// <see cref="IAnnotationSurface.InkScale"/> is the point of the exercise. A 2px ring drawn at 4x on a
/// plate whose title, caption and colorbar have all quadrupled is the only thing in the figure that
/// shrank — which is exactly the resolution someone chose because they wanted the marks readable.
/// </summary>
public sealed class FitsExportSurface : IAnnotationSurface
{
    private readonly FitsRegion _region;
    private readonly double _plateWidth, _plateHeight;
    private readonly WcsInfo? _wcs;
    private readonly int _imageHeight;

    /// <param name="region">The area of the image the plate shows, in display pixels.</param>
    /// <param name="plateWidth">The frame's width on the plate.</param>
    /// <param name="plateHeight">The frame's height on the plate.</param>
    /// <param name="wcs">The image's WCS, for sky-anchored marks.</param>
    /// <param name="imageHeight">The image height, which reconciles the FITS and display pixel conventions.</param>
    public FitsExportSurface(
        FitsRegion region, double plateWidth, double plateHeight, WcsInfo? wcs, int imageHeight)
    {
        _region = region;
        _plateWidth = plateWidth;
        _plateHeight = plateHeight;
        _wcs = wcs;
        _imageHeight = imageHeight;
    }

    /// <summary>
    /// How much bigger the plate is than the screen would be. Set to the export's scale factor, so every
    /// stroke, label and leader is drawn at the size it would have been on screen, multiplied.
    /// </summary>
    public double InkScale { get; init; } = 1.0;

    /// <summary>Plate pixels per image pixel across. Equal to the vertical one when the frame keeps the aspect.</summary>
    private double ScaleX => _region.Width > 0 ? _plateWidth / _region.Width : 0;
    private double ScaleY => _region.Height > 0 ? _plateHeight / _region.Height : 0;

    public (double X, double Y)? Project(AnnotationAnchor anchor)
    {
        if (!anchor.IsValid || ScaleX <= 0 || ScaleY <= 0) return null;

        var display = anchor.Space switch
        {
            AnchorSpace.ImagePixel => (X: anchor.X, Y: anchor.Y),
            AnchorSpace.Sky => ToDisplay(anchor.X, anchor.Y),

            // A cube mark on a FITS plate belongs to another viewer.
            _ => null,
        };

        if (display is not { } at) return null;

        // A mark outside the region is NOT on this plate. Skipped rather than clamped to the border,
        // where it would sit on the frame edge claiming to point at something inside the picture.
        if (at.X < _region.X || at.X > _region.Right || at.Y < _region.Y || at.Y > _region.Bottom)
            return null;

        return ((at.X - _region.X) * ScaleX, (at.Y - _region.Y) * ScaleY);
    }

    private (double X, double Y)? ToDisplay(double raDeg, double decDeg)
    {
        if (_wcs is not { IsValid: true } wcs || _imageHeight <= 0) return null;
        if (wcs.WorldToPixel(raDeg, decDeg) is not { } p) return null;

        return (p.Px - 1, _imageHeight - 1 - (p.Py - 1));
    }

    /// <summary>
    /// Plate pixels per unit of the anchor's space. Derived rather than measured: the mapping is a plain
    /// scale, so the two agree exactly — and deriving it keeps working for a mark just outside the
    /// region, where a stepped point would be clipped and answer nothing.
    /// </summary>
    public double UnitsToPixels(AnnotationAnchor anchor)
    {
        if (ScaleX <= 0) return 1.0;

        switch (anchor.Space)
        {
            case AnchorSpace.ImagePixel:
                return ScaleX;

            case AnchorSpace.Sky:
            {
                // Degrees to image pixels, measured through the WCS in DECLINATION — a degree of RA is
                // not a degree on the sky except at the equator — and then image pixels to plate pixels.
                if (ToDisplay(anchor.X, anchor.Y) is not { } here) return 1.0;
                if (ToDisplay(anchor.X, Math.Clamp(anchor.Y + SkyStep, -90, 90)) is not { } there) return 1.0;

                var pixelsPerDegree = Math.Sqrt(Math.Pow(there.X - here.X, 2) + Math.Pow(there.Y - here.Y, 2)) / SkyStep;
                var scale = pixelsPerDegree * ScaleX;
                return double.IsFinite(scale) && scale > 0 ? scale : 1.0;
            }

            default:
                return 1.0;
        }
    }

    /// <summary>Small enough to be locally linear, large enough not to be lost in the projection.</summary>
    private const double SkyStep = 0.001;
}
