namespace CanfarDesktop.Services.Fits;

/// <summary>
/// Pure static stretch functions for normalizing FITS pixel values to [0, 1].
/// </summary>
public static class ImageStretcher
{
    public enum StretchMode { Linear, Log, Sqrt, Squared, Asinh }

    /// <summary>
    /// Normalize a pixel value to [0, 1] using the specified stretch and min/max cuts.
    /// </summary>
    public static float Stretch(float value, float min, float max, StretchMode mode)
    {
        if (max <= min) return 0.5f;
        if (!float.IsFinite(value)) return 0f;

        // Clamp to cut range
        var normalized = Math.Clamp((value - min) / (max - min), 0f, 1f);

        return mode switch
        {
            StretchMode.Linear => normalized,
            StretchMode.Log => MathF.Log10(1 + 9 * normalized) / MathF.Log10(10f),
            StretchMode.Sqrt => MathF.Sqrt(normalized),
            StretchMode.Squared => normalized * normalized,
            StretchMode.Asinh => MathF.Asinh(10 * normalized) / MathF.Asinh(10f),
            _ => normalized,
        };
    }

    /// <summary>
    /// Apply stretch to an entire pixel array. Returns normalized [0, 1] array.
    /// </summary>
    public static float[] StretchArray(float[] pixels, float min, float max, StretchMode mode)
    {
        var result = new float[pixels.Length];
        for (var i = 0; i < pixels.Length; i++)
            result[i] = Stretch(pixels[i], min, max, mode);
        return result;
    }
}
