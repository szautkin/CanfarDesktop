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
        float maxCut)
    {
        var width = image.Width;
        var height = image.Height;
        var pixels = image.Pixels;
        var bgra = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
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
