using System.Numerics;

namespace CanfarDesktop.Services.CubeViewer;

/// <summary>
/// Colormap and opacity-transfer-function lookup tables for the cube volume
/// renderer. Mirrors the macOS pipeline: a 256-entry RGBA colormap texture and a
/// 256-entry alpha ramp built from transfer-function control points (see
/// <c>CubeVolumeRenderer.setColormap</c> / <c>setTransferFunction</c> in the
/// Swift source).
/// </summary>
internal static class CubeColormaps
{
    /// <summary>
    /// Build the 256×1 RGBA8 inferno colormap (matplotlib's perceptually-uniform
    /// inferno), matching the macOS default colormap for the cube viewer. Returns
    /// a tightly packed byte array of length 256·4 in RGBA order.
    /// </summary>
    public static byte[] Inferno()
    {
        // 17 anchor stops sampled from matplotlib inferno at t = i/16. Linearly
        // interpolated to 256 entries. Perceptually close to the real LUT and far
        // better than a procedural approximation for scientific imagery.
        ReadOnlySpan<(float r, float g, float b)> stops =
        [
            (0.001462f, 0.000466f, 0.013866f), // 0.000
            (0.046915f, 0.030324f, 0.150164f), // 0.0625
            (0.142378f, 0.046242f, 0.308553f), // 0.125
            (0.258234f, 0.038571f, 0.406485f), // 0.1875
            (0.366529f, 0.071579f, 0.431994f), // 0.250
            (0.472328f, 0.110547f, 0.428334f), // 0.3125
            (0.578304f, 0.148039f, 0.404411f), // 0.375
            (0.682656f, 0.189501f, 0.360757f), // 0.4375
            (0.780517f, 0.243327f, 0.299523f), // 0.500
            (0.865006f, 0.316822f, 0.226055f), // 0.5625
            (0.929644f, 0.411479f, 0.145367f), // 0.625
            (0.970919f, 0.522853f, 0.058367f), // 0.6875
            (0.987622f, 0.645320f, 0.039886f), // 0.750
            (0.978806f, 0.774545f, 0.176037f), // 0.8125
            (0.950018f, 0.903409f, 0.380271f), // 0.875
            (0.954529f, 0.972590f, 0.612366f), // 0.9375
            (0.988362f, 0.998364f, 0.644924f), // 1.000
        ];

        var rgba = new byte[256 * 4];
        int segCount = stops.Length - 1;
        for (int i = 0; i < 256; i++)
        {
            float t = i / 255f * segCount;
            int k = Math.Min((int)t, segCount - 1);
            float f = t - k;
            var a = stops[k];
            var b = stops[k + 1];
            byte R = ToByte(a.r + (b.r - a.r) * f);
            byte G = ToByte(a.g + (b.g - a.g) * f);
            byte B = ToByte(a.b + (b.b - a.b) * f);
            int o = i * 4;
            rgba[o + 0] = R;
            rgba[o + 1] = G;
            rgba[o + 2] = B;
            rgba[o + 3] = 255;
        }
        return rgba;
    }

    /// <summary>
    /// Default opacity transfer-function control points (value ∈ [0,1] → alpha ∈
    /// [0,1]), copied from <c>CubeViewerModel.transferFunction</c> in the macOS
    /// source. Low values fade out (suppress noise floor), high values become opaque.
    /// </summary>
    public static readonly Vector2[] DefaultTransferFunction =
    [
        new(0.0f, 0.0f),
        new(0.45f, 0.05f),
        new(0.75f, 0.45f),
        new(1.0f, 1.0f),
    ];

    /// <summary>
    /// Build a 256-entry R8 alpha ramp from transfer-function control points.
    /// Direct port of the Swift <c>setTransferFunction</c> piecewise-linear
    /// interpolation. Returns a byte array of length 256 (single channel).
    /// </summary>
    public static byte[] TransferRamp(IReadOnlyList<Vector2> points)
    {
        var sorted = points.OrderBy(p => p.X).ToArray();
        var ramp = new byte[256];
        if (sorted.Length == 0) return ramp;

        Vector2 first = sorted[0];
        Vector2 last = sorted[^1];
        for (int i = 0; i < 256; i++)
        {
            float x = i / 255f;
            float a = first.Y;
            if (x >= last.X)
            {
                a = last.Y;
            }
            else
            {
                for (int j = 0; j < sorted.Length - 1; j++)
                {
                    if (x >= sorted[j].X && x < sorted[j + 1].X)
                    {
                        float span = MathF.Max(sorted[j + 1].X - sorted[j].X, 1e-6f);
                        float f = (x - sorted[j].X) / span;
                        a = sorted[j].Y * (1 - f) + sorted[j + 1].Y * f;
                        break;
                    }
                }
            }
            ramp[i] = ToByte(a);
        }
        return ramp;
    }

    private static byte ToByte(float v) => (byte)(Math.Clamp(v, 0f, 1f) * 255f + 0.5f);
}
