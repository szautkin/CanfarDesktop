namespace CanfarDesktop.Models.Fits;

/// <summary>
/// A rectangle of an image, in DISPLAY pixels — 0-based, y down from the top, the same coordinates the
/// viewer's crosshair and marks use. Not FITS pixels: everything that hands one of these around is
/// looking at the picture rather than at the file.
///
/// A region is what an export is OF. There are four ways to say which one you mean — the view on
/// screen, a pixel box, a circle on the sky, or a mark someone drew — and they all end up here, so the
/// renderer and the plate only ever deal with one thing.
/// </summary>
public readonly record struct FitsRegion(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double CentreX => X + Width / 2;
    public double CentreY => Y + Height / 2;

    public bool IsValid => double.IsFinite(X) && double.IsFinite(Y)
                        && double.IsFinite(Width) && double.IsFinite(Height)
                        && Width > 0 && Height > 0;

    /// <summary>A region from two corners, in any order — a drag can go up and to the left.</summary>
    public static FitsRegion FromCorners(double x1, double y1, double x2, double y2)
        => new(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));

    /// <summary>The whole image.</summary>
    public static FitsRegion WholeImage(int width, int height) => new(0, 0, width, height);

    /// <summary>A square region centred on a point, of the given half-size.</summary>
    public static FitsRegion Around(double centreX, double centreY, double halfWidth, double halfHeight)
        => new(centreX - halfWidth, centreY - halfHeight, halfWidth * 2, halfHeight * 2);

    /// <summary>
    /// A circle on the sky, as the box that contains it.
    ///
    /// The radius is converted by MEASURING it: the centre and a point one radius north are both put
    /// through the WCS, and the distance between them in pixels is the radius in pixels. Dividing the
    /// radius by a nominal pixel scale would be near enough on a small field and wrong on a large one,
    /// where the scale is not constant across the frame.
    ///
    /// North rather than east, because a degree of RA is not a degree on the sky except at the equator.
    /// </summary>
    public static FitsRegion? FromSkyCircle(WcsInfo? wcs, int imageHeight, double raDeg, double decDeg, double radiusDeg)
    {
        if (wcs is not { IsValid: true } || imageHeight <= 0) return null;
        if (!double.IsFinite(radiusDeg) || radiusDeg <= 0) return null;

        var centre = ToDisplay(wcs, imageHeight, raDeg, decDeg);
        var edge = ToDisplay(wcs, imageHeight, raDeg, Math.Clamp(decDeg + radiusDeg, -90, 90));
        if (centre is not { } c || edge is not { } e) return null;

        var radiusPixels = Math.Sqrt(Math.Pow(e.X - c.X, 2) + Math.Pow(e.Y - c.Y, 2));
        if (!double.IsFinite(radiusPixels) || radiusPixels <= 0) return null;

        return Around(c.X, c.Y, radiusPixels, radiusPixels);
    }

    /// <summary>
    /// A sky position as a display pixel. The same conversion the viewer makes, and the reason it is
    /// spelled out here too: WorldToPixel answers 1-based FITS pixels counting up from the bottom.
    /// </summary>
    private static (double X, double Y)? ToDisplay(WcsInfo wcs, int imageHeight, double raDeg, double decDeg)
        => wcs.WorldToPixel(raDeg, decDeg) is { } p ? (p.Px - 1, imageHeight - 1 - (p.Py - 1)) : null;

    /// <summary>
    /// Grown by a fraction of its own size, so an exported figure has some sky around its subject rather
    /// than the subject jammed against the frame.
    /// </summary>
    public FitsRegion Padded(double fraction)
    {
        if (!IsValid || !double.IsFinite(fraction) || fraction <= 0) return this;

        var padX = Width * fraction;
        var padY = Height * fraction;
        return new FitsRegion(X - padX, Y - padY, Width + padX * 2, Height + padY * 2);
    }

    /// <summary>
    /// Trimmed to the image, keeping whatever overlaps it. Null when the region misses the image
    /// entirely — which is an answer ("that is not on this image"), not something to clamp into a
    /// one-pixel sliver at the edge and export.
    /// </summary>
    public FitsRegion? ClampTo(int imageWidth, int imageHeight)
    {
        if (!IsValid || imageWidth <= 0 || imageHeight <= 0) return null;

        var left = Math.Max(0, X);
        var top = Math.Max(0, Y);
        var right = Math.Min(imageWidth, Right);
        var bottom = Math.Min(imageHeight, Bottom);

        if (right - left < 1 || bottom - top < 1) return null;
        return new FitsRegion(left, top, right - left, bottom - top);
    }
}
