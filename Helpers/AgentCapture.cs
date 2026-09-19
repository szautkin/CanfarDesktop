namespace CanfarDesktop.Helpers;

/// <summary>
/// How a position in a returned capture maps back to the image it is of.
///
/// This is the point of the exercise. A picture on its own lets an agent SAY something about what it
/// sees; the transform lets it POINT — turn "the bright source at (412, 233) in this image" into a
/// display pixel, and from there, through the WCS, into a sky coordinate. Without it every capture is
/// a dead end that has to be described in words.
///
/// <para>A capture is a plain scale and offset of a rectangular region: no rotation, no perspective.
/// <c>imageX = OriginX + captureX / PixelsPerImagePixel</c>, and the same for Y. Display pixels, which
/// is what the viewer, the crosshair and the marks use — <see cref="Models.Fits.PixelConvention"/>
/// carries it the rest of the way to sky.</para>
/// </summary>
public readonly record struct CaptureTransform(double OriginX, double OriginY, double PixelsPerImagePixel)
{
    /// <summary>A position in the capture as a display pixel of the image.</summary>
    public (double X, double Y) ToImage(double captureX, double captureY)
        => PixelsPerImagePixel <= 0
            ? (OriginX, OriginY)
            : (OriginX + captureX / PixelsPerImagePixel, OriginY + captureY / PixelsPerImagePixel);

    /// <summary>A display pixel of the image as a position in the capture. The inverse of <see cref="ToImage"/>.</summary>
    public (double X, double Y) ToCapture(double imageX, double imageY)
        => ((imageX - OriginX) * PixelsPerImagePixel, (imageY - OriginY) * PixelsPerImagePixel);
}

/// <summary>
/// The size a capture was rendered at, and whether that is the size it was asked for.
/// <see cref="Note"/> is non-null exactly when something was reduced, so the reply can say why rather
/// than silently handing back a smaller picture than the caller expected.
/// </summary>
public readonly record struct CaptureSize(int Width, int Height, double Scale, string? Note)
{
    public bool WasReduced => Note is not null;
}

/// <summary>
/// What bounds an agent-facing capture.
///
/// Two limits, because they catch different mistakes. The pixel cap is about VALUE: a 4000px capture
/// costs an agent roughly sixteen times the context of a 1000px one and tells it nothing more, because
/// nothing downstream resolves that finely. The byte cap is about the wire: MCP clients reject a
/// response body past about a megabyte, and a capture that arrives as an error is worse than a small
/// one.
/// </summary>
public static class AgentCapture
{
    /// <summary>Longest edge, in pixels, unless the user raises it. Enough to read structure by.</summary>
    public const int DefaultMaxPixels = 1024;

    /// <summary>The ceiling the setting itself is clamped to — past this the wire cap binds anyway.</summary>
    public const int MaxPixelsCeiling = 4096;

    /// <summary>Default byte budget for the encoded image (~928 KB as base64, under the ~1 MB client limit).</summary>
    public const int DefaultMaxBytes = 680 * 1024;

    /// <summary>
    /// The size to render a region at: never enlarged, reduced to fit the pixel cap, then reduced
    /// again if the estimate says it would not fit the byte budget.
    ///
    /// <para>Never enlarged, because upscaling invents detail the data does not have — a 64x64 cutout
    /// stays 64x64 rather than being blown up to look like a 1024px observation of something.</para>
    /// </summary>
    public static CaptureSize Fit(int regionWidth, int regionHeight, int maxPixels, int maxBytes)
    {
        if (regionWidth <= 0 || regionHeight <= 0) return new CaptureSize(0, 0, 1.0, "the region is empty");

        maxPixels = Math.Clamp(maxPixels <= 0 ? DefaultMaxPixels : maxPixels, 1, MaxPixelsCeiling);
        maxBytes = maxBytes <= 0 ? DefaultMaxBytes : maxBytes;

        string? note = null;
        var scale = 1.0;

        var longest = Math.Max(regionWidth, regionHeight);
        if (longest > maxPixels)
        {
            scale = (double)maxPixels / longest;
            note = $"downscaled to {maxPixels}px on its longest edge";
        }

        // Then the wire. Estimated from the raw pixels rather than the encoded size, which is not known
        // until it is encoded: PNG of a rendered astronomical frame runs well under 4 bytes a pixel, so
        // this is conservative in the direction that matters.
        if (EstimatedBytes(regionWidth, regionHeight, scale) > maxBytes)
        {
            var byScale = Math.Sqrt(maxBytes / (double)EstimatedBytes(regionWidth, regionHeight, 1.0));
            if (byScale < scale)
            {
                scale = byScale;
                note = $"downscaled to fit {maxBytes / 1024} KB";
            }
        }

        var w = Math.Max(1, (int)Math.Round(regionWidth * scale));
        var h = Math.Max(1, (int)Math.Round(regionHeight * scale));
        return new CaptureSize(w, h, scale, note);
    }

    /// <summary>Bytes a capture of this size is assumed to cost. BGRA, before PNG has had a go at it.</summary>
    private static long EstimatedBytes(int width, int height, double scale)
        => (long)(width * scale) * (long)(height * scale) * 4;

    /// <summary>
    /// The transform for a capture of <paramref name="region"/> rendered at <paramref name="scale"/>.
    /// The region's own origin is the offset; the scale is how many capture pixels one image pixel
    /// became.
    /// </summary>
    public static CaptureTransform TransformFor(double regionX, double regionY, double scale)
        => new(regionX, regionY, scale);
}
