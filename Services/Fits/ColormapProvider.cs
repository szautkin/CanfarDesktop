namespace CanfarDesktop.Services.Fits;

using Windows.UI;

/// <summary>
/// Generates 256-entry Color lookup tables for image display.
/// </summary>
public static class ColormapProvider
{
    public enum ColormapName { Grayscale, Inverted, Heat, Cool, Viridis }

    public static Color[] GetColormap(ColormapName name) => name switch
    {
        ColormapName.Grayscale => GenerateGrayscale(),
        ColormapName.Inverted => GenerateInverted(),
        ColormapName.Heat => GenerateHeat(),
        ColormapName.Cool => GenerateCool(),
        ColormapName.Viridis => GenerateViridis(),
        _ => GenerateGrayscale(),
    };

    private static Color[] GenerateGrayscale()
    {
        var lut = new Color[256];
        for (var i = 0; i < 256; i++)
            lut[i] = Color.FromArgb(255, (byte)i, (byte)i, (byte)i);
        return lut;
    }

    private static Color[] GenerateInverted()
    {
        var lut = new Color[256];
        for (var i = 0; i < 256; i++)
        {
            var v = (byte)(255 - i);
            lut[i] = Color.FromArgb(255, v, v, v);
        }
        return lut;
    }

    private static Color[] GenerateHeat()
    {
        var lut = new Color[256];
        for (var i = 0; i < 256; i++)
        {
            var t = i / 255f;
            var r = (byte)Math.Clamp(t * 3 * 255, 0, 255);
            var g = (byte)Math.Clamp((t - 0.33f) * 3 * 255, 0, 255);
            var b = (byte)Math.Clamp((t - 0.67f) * 3 * 255, 0, 255);
            lut[i] = Color.FromArgb(255, r, g, b);
        }
        return lut;
    }

    private static Color[] GenerateCool()
    {
        var lut = new Color[256];
        for (var i = 0; i < 256; i++)
        {
            var t = i / 255f;
            var r = (byte)(t * 255);
            var g = (byte)((1 - t) * 255);
            var b = (byte)(255);
            lut[i] = Color.FromArgb(255, r, g, b);
        }
        return lut;
    }

    private static Color[] GenerateViridis()
    {
        // Simplified viridis approximation (purple → teal → yellow)
        var lut = new Color[256];
        for (var i = 0; i < 256; i++)
        {
            var t = i / 255f;
            var r = (byte)(255 * Math.Clamp(1.5f * t - 0.2f, 0, 1));
            var g = (byte)(255 * Math.Clamp(0.3f + 0.7f * t * (1 - 0.5f * t), 0, 1));
            var b = (byte)(255 * Math.Clamp(0.5f - 0.5f * t + 0.3f * t * t, 0, 1));
            lut[i] = Color.FromArgb(255, r, g, b);
        }
        return lut;
    }
}
