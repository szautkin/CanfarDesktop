namespace CanfarDesktop.Services.Fits;

using CanfarDesktop.Models.Fits;

/// <summary>
/// Renders FITS image data to a BGRA8 byte array suitable for display.
/// Applies stretch + colormap + min/max cuts. Flips Y-axis (FITS origin = bottom-left).
/// Thread-safe: all static, no mutable state.
/// </summary>
public static class FitsRenderer
{
    /// <summary>
    /// Render FITS pixels to BGRA8 byte array.
    /// </summary>
    /// <param name="image">Source image data.</param>
    /// <param name="stretch">Stretch function to apply.</param>
    /// <param name="colormap">256-entry color LUT.</param>
    /// <param name="minCut">Low cut value.</param>
    /// <param name="maxCut">High cut value.</param>
    /// <returns>BGRA8 byte array (4 bytes per pixel, row-major, Y-flipped).</returns>
    public static byte[] Render(
        FitsImageData image,
        ImageStretcher.StretchMode stretch,
        Windows.UI.Color[] colormap,
        float minCut,
        float maxCut,
        CancellationToken ct = default)
    {
        var width = image.Width;
        var height = image.Height;
        var pixels = image.Pixels;
        var bgra = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            if (y % 64 == 0) ct.ThrowIfCancellationRequested();
            // Flip Y: FITS row 0 = bottom, display row 0 = top
            var srcRow = height - 1 - y;
            var srcOffset = srcRow * width;
            var dstOffset = y * width * 4;

            for (var x = 0; x < width; x++)
            {
                var value = pixels[srcOffset + x];
                var stretched = ImageStretcher.Stretch(value, minCut, maxCut, stretch);
                var lutIndex = Math.Clamp((int)(stretched * 255), 0, 255);
                var color = colormap[lutIndex];

                var dst = dstOffset + x * 4;
                bgra[dst + 0] = color.B;     // Blue
                bgra[dst + 1] = color.G;     // Green
                bgra[dst + 2] = color.R;     // Red
                bgra[dst + 3] = 255;         // Alpha
            }
        }

        return bgra;
    }


    /// <summary>
    /// Render one REGION of an image at an arbitrary output size — what an exported figure is made of.
    ///
    /// Rendered from the pixel data through a substituted geometry, NOT captured from the screen. A
    /// screenshot of the viewer carries whatever the viewer happens to be: the zoom someone left it at,
    /// a crosshair, a panel edge, and the screen's own resampling. A figure has to be a picture of the
    /// data, at the resolution the author asked for.
    ///
    /// Sampling is NEAREST NEIGHBOUR. Smoothing a 4x export would invent values between the pixels, and
    /// on an image where a point source IS one pixel that is a figure making a claim the data does not
    /// support. Blocky is honest.
    /// </summary>
    /// <param name="image">Source image data.</param>
    /// <param name="region">The area to render, in DISPLAY pixels (0-based, y down from the top).</param>
    /// <param name="outWidth">Output width in pixels.</param>
    /// <param name="outHeight">Output height in pixels.</param>
    public static byte[]? RenderRegion(
        FitsImageData image,
        FitsRegion region,
        int outWidth,
        int outHeight,
        ImageStretcher.StretchMode stretch,
        Windows.UI.Color[] colormap,
        float minCut,
        float maxCut,
        CancellationToken ct = default)
    {
        if (outWidth <= 0 || outHeight <= 0) return null;
        if (region.ClampTo(image.Width, image.Height) is not { } area) return null;

        var bgra = new byte[(long)outWidth * outHeight * 4];
        var stepX = area.Width / outWidth;
        var stepY = area.Height / outHeight;

        for (var oy = 0; oy < outHeight; oy++)
        {
            if (oy % 64 == 0) ct.ThrowIfCancellationRequested();

            // Sample the middle of each output pixel's footprint rather than its corner: sampling the
            // corner shifts the whole picture half an output pixel, which at 4x is visible against an
            // overlay drawn from the same coordinates.
            var displayY = area.Y + (oy + 0.5) * stepY;

            // The stored rows run bottom-up; the display counts down from the top.
            var srcRow = image.Height - 1 - (int)Math.Clamp(displayY, 0, image.Height - 1);
            var srcOffset = srcRow * image.Width;
            var dstOffset = oy * outWidth * 4;

            for (var ox = 0; ox < outWidth; ox++)
            {
                var displayX = area.X + (ox + 0.5) * stepX;
                var srcX = (int)Math.Clamp(displayX, 0, image.Width - 1);

                var stretched = ImageStretcher.Stretch(image.Pixels[srcOffset + srcX], minCut, maxCut, stretch);
                var color = colormap[Math.Clamp((int)(stretched * 255), 0, 255)];

                var dst = dstOffset + ox * 4;
                bgra[dst + 0] = color.B;
                bgra[dst + 1] = color.G;
                bgra[dst + 2] = color.R;
                bgra[dst + 3] = 255;
            }
        }

        return bgra;
    }

    /// <summary>
    /// Compute auto-cut values using percentile clipping.
    /// </summary>
    public static (float min, float max) AutoCut(FitsImageData image, float lowPercentile = 0.5f, float highPercentile = 99.5f)
    {
        // Sample up to 100K pixels for performance
        var pixels = image.Pixels;
        var step = Math.Max(1, pixels.Length / 100_000);
        var samples = new List<float>(100_000);

        for (var i = 0; i < pixels.Length; i += step)
        {
            if (float.IsFinite(pixels[i]))
                samples.Add(pixels[i]);
        }

        if (samples.Count == 0) return (0, 1);

        samples.Sort();
        var lowIdx = Math.Clamp((int)(samples.Count * lowPercentile / 100f), 0, samples.Count - 1);
        var highIdx = Math.Clamp((int)(samples.Count * highPercentile / 100f), 0, samples.Count - 1);

        return (samples[lowIdx], samples[highIdx]);
    }
}
