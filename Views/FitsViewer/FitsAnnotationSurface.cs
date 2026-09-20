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
    private readonly Func<(int Width, int Height)> _imageSize;

    /// <param name="imageToScreen">A DISPLAY pixel (0-based, y down from the top) to a canvas point.</param>
    /// <param name="wcs">The image's WCS, or null when it has none.</param>
    /// <param name="imageSize">
    /// The image's pixel dimensions. The height converts between the two pixel conventions in play — see
    /// <see cref="ProjectSky"/> — and both together are the frame a mark has to be inside to be on this
    /// image at all.
    ///
    /// <para>One function rather than two because the width and the height change TOGETHER, when the HDU
    /// does. Asked separately they could be answered from different extensions, and a frame half a
    /// megapixel wide and a quarter of one tall is a bounds check that passes what it should reject.</para>
    ///
    /// <para>A function rather than a value because a surface is built once per render and the HDU
    /// changes under it.</para>
    /// </param>
    public FitsAnnotationSurface(
        Func<double, double, (double X, double Y)> imageToScreen,
        Func<WcsInfo?> wcs,
        Func<(int Width, int Height)> imageSize)
    {
        _imageToScreen = imageToScreen;
        _wcs = wcs;
        _imageSize = imageSize;
    }

    /// <summary>1.0 on screen. An export plate constructs its own surface with its own factor.</summary>
    public double InkScale { get; init; } = 1.0;

    /// <summary>
    /// Where the mark goes on the canvas, or null when it is not on this image.
    ///
    /// <para>The bounds test is the point. A sky anchor is projected through whatever WCS the CURRENT
    /// extension carries, and a mosaic's extensions each have their own — so a mark pinned on one CCD,
    /// asked of another, comes back as a perfectly finite pixel some thousands of rows off the frame.
    /// The canvas has no edges to stop it, so it was drawn anyway: parked at a corner, over the image,
    /// pointing at nothing. Off the frame is an ANSWER, and it is the same one the export plate has
    /// always given for a mark outside its region.</para>
    /// </summary>
    public (double X, double Y)? Project(AnnotationAnchor anchor)
    {
        if (ToDisplay(anchor) is not { } at) return null;
        if (Frame() is not { } frame || !frame.Contains(at.X, at.Y)) return null;

        return _imageToScreen(at.X, at.Y);
    }

    /// <summary>The image's own extent, in display pixels, or null when nothing is loaded.</summary>
    private FitsRegion? Frame()
    {
        var (width, height) = _imageSize();
        return width > 0 && height > 0 ? FitsRegion.WholeImage(width, height) : null;
    }

    /// <summary>
    /// The same projection WITHOUT the bounds test — what <see cref="UnitsToPixels"/> needs.
    ///
    /// The scale is measured by projecting a point one unit away, and for a mark sitting on the edge of
    /// the frame that point is off it. Clipped, the measurement would fail and fall back to one pixel
    /// per unit, so a mark would shrink to a dot at exactly the moment it reached the border. The plate
    /// avoids this by deriving its scale instead; here the mapping carries a zoom, a rotation and a
    /// parity flip, so it is measured — and measured against an unclipped projection.
    /// </summary>
    private (double X, double Y)? ProjectUnclipped(AnnotationAnchor anchor)
        => ToDisplay(anchor) is { } at ? _imageToScreen(at.X, at.Y) : null;

    /// <summary>The anchor as a display pixel, whatever space it is pinned in.</summary>
    private (double X, double Y)? ToDisplay(AnnotationAnchor anchor)
    {
        if (!anchor.IsValid) return null;

        return anchor.Space switch
        {
            AnchorSpace.ImagePixel => (anchor.X, anchor.Y),
            AnchorSpace.Sky => ProjectSky(anchor.X, anchor.Y),

            // A cube mark on a FITS canvas. Not an error and not clamped: it belongs to another
            // viewer's space, and a clamped mark points at the wrong thing.
            _ => null,
        };
    }

    /// <summary>
    /// A sky position as a display pixel.
    ///
    /// The two ends count pixels differently, and mixing them is how a mark ends up mirrored and one
    /// pixel out — near enough to look like a rendering wobble rather than a coordinate bug. Both
    /// directions go through <see cref="PixelConvention"/>, which is where the FITS, array and display
    /// conventions are named and each paired with its inverse, so this and <see cref="SkyAt"/> cancel
    /// exactly rather than nearly.
    /// </summary>
    private (double X, double Y)? ProjectSky(double raDeg, double decDeg)
    {
        if (_wcs() is not { } wcs) return null;

        var height = _imageSize().Height;
        if (height <= 0) return null;

        return PixelConvention.DisplayOfSky(wcs, height, raDeg, decDeg);
    }

    /// <summary>
    /// A display pixel as a sky position — the inverse of <see cref="ProjectSky"/>, and the conversion a
    /// viewer needs when it turns a press into an anchor. Here rather than in the page so that the two
    /// directions sit together and cannot drift apart.
    /// </summary>
    public static AnnotationAnchor? SkyAt(WcsInfo? wcs, int imageHeight, double displayX, double displayY)
    {
        if (wcs is not { IsValid: true } || imageHeight <= 0) return null;

        var (ra, dec) = PixelConvention.SkyAtDisplay(wcs, imageHeight, displayX, displayY);
        var anchor = AnnotationAnchor.Sky(ra, dec);
        return anchor.IsValid ? anchor : null;
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
        if (ProjectUnclipped(anchor) is not { } here) return 1.0;

        var stepped = anchor.Space switch
        {
            AnchorSpace.ImagePixel => AnnotationAnchor.ImagePixel(anchor.X + 1, anchor.Y),

            // A degree of RA is not a degree on the sky except at the equator, so the step is taken in
            // Dec — where one degree is one degree everywhere, and a circle drawn with it is the size
            // it says it is.
            AnchorSpace.Sky => AnnotationAnchor.Sky(anchor.X, Math.Clamp(anchor.Y + SkyStep, -90, 90)),
            _ => null,
        };

        if (stepped is null || ProjectUnclipped(stepped) is not { } there) return 1.0;

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
