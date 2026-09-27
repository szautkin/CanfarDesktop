namespace CanfarDesktop.Models.Fits;

/// <summary>
/// The three ways this app counts pixels, named, with every conversion paired with its inverse.
///
/// They were not named before, and the arithmetic was written out at each site that needed it —
/// <c>PixelToWorld(x + 1, height - 1 - y + 1)</c> and its several near-variants, each with a comment
/// re-deriving why. Two of those comments apologised for being a copy. The risk is not that any one
/// of them is wrong today; it is that a crosshair and a mark reading the same position must cancel
/// EXACTLY, and hand-written pairs cancel only as long as every copy is edited together.
///
/// <list type="bullet">
/// <item><b>FITS</b> — 1-based, counting pixel CENTRES, row 1 at the bottom. What CRPIX is stated in,
/// and therefore the only convention <see cref="WcsInfo.PixelToWorld"/> and
/// <see cref="WcsInfo.WorldToPixel"/> speak.</item>
/// <item><b>Array</b> — 0-based index into the decoded data, row 0 at the bottom. What the parser and
/// the MCP tools pass, because it is an index into memory.</item>
/// <item><b>Display</b> — 0-based, row 0 at the TOP, because the renderer flips Y to draw. What the
/// canvas, the crosshair, marks and <see cref="FitsRegion"/> use.</item>
/// </list>
///
/// Only the display conversions need the image height; it is the flip that needs to know where the
/// bottom is.
/// </summary>
public static class PixelConvention
{
    /// <summary>Array index → FITS pixel. The 1-based shift, nothing else.</summary>
    public static (double X, double Y) ArrayToFits(double x, double y) => (x + 1, y + 1);

    /// <summary>FITS pixel → array index. The inverse of <see cref="ArrayToFits"/>.</summary>
    public static (double X, double Y) FitsToArray(double x, double y) => (x - 1, y - 1);

    /// <summary>Display pixel → array index. The Y flip, nothing else.</summary>
    public static (double X, double Y) DisplayToArray(double x, double y, int imageHeight)
        => (x, imageHeight - 1 - y);

    /// <summary>
    /// Array index → display pixel. Its own inverse for a given height, which is what makes a
    /// round trip through the canvas exact rather than nearly exact.
    /// </summary>
    public static (double X, double Y) ArrayToDisplay(double x, double y, int imageHeight)
        => (x, imageHeight - 1 - y);

    /// <summary>Display pixel → FITS pixel: flip, then shift.</summary>
    public static (double X, double Y) DisplayToFits(double x, double y, int imageHeight)
    {
        var (ax, ay) = DisplayToArray(x, y, imageHeight);
        return ArrayToFits(ax, ay);
    }

    /// <summary>FITS pixel → display pixel. The inverse of <see cref="DisplayToFits"/>.</summary>
    public static (double X, double Y) FitsToDisplay(double x, double y, int imageHeight)
    {
        var (ax, ay) = FitsToArray(x, y);
        return ArrayToDisplay(ax, ay, imageHeight);
    }

    /// <summary>
    /// The sky position at a display pixel — the conversion the crosshair, the readout and the mark
    /// renderer all want, in one place so they cannot drift apart.
    /// </summary>
    public static (double Ra, double Dec) SkyAtDisplay(WcsInfo wcs, int imageHeight, double x, double y)
    {
        var (fx, fy) = DisplayToFits(x, y, imageHeight);
        return wcs.PixelToWorld(fx, fy);
    }

    /// <summary>
    /// The display pixel of a sky position. Null when the WCS cannot place it — a singular matrix, or
    /// a coordinate outside the projection's domain.
    /// </summary>
    public static (double X, double Y)? DisplayOfSky(WcsInfo wcs, int imageHeight, double ra, double dec)
        => wcs.WorldToPixel(ra, dec) is { } p ? FitsToDisplay(p.Px, p.Py, imageHeight) : null;

    /// <summary>Whether a display pixel lies on an image of this size.</summary>
    public static bool IsOnImage(double x, double y, int width, int height)
        => x >= 0 && x < width && y >= 0 && y < height;
}
