using System.Numerics;

namespace CanfarDesktop.Services.CubeViewer;

/// <summary>The cube viewer's selectable colormaps (mirrors the macOS Colormap enum).</summary>
public enum CubeColormap { Grayscale, Inverted, Heat, Cool, Viridis, Inferno, Magma, Plasma }

/// <summary>
/// Colormap and opacity-transfer-function lookup tables for the cube volume
/// renderer. Mirrors the macOS pipeline: a 256-entry RGBA colormap texture and a
/// 256-entry alpha ramp built from transfer-function control points (see
/// <c>CubeVolumeRenderer.setColormap</c> / <c>setTransferFunction</c> in the
/// Swift source).
/// </summary>
internal static class CubeColormaps
{
    /// <summary>Display name for the picker UI.</summary>
    public static string DisplayName(CubeColormap c) => c switch
    {
        CubeColormap.Grayscale => "Grayscale",
        CubeColormap.Inverted => "Inverted",
        CubeColormap.Heat => "Heat",
        CubeColormap.Cool => "Cool",
        CubeColormap.Viridis => "Viridis",
        CubeColormap.Inferno => "Inferno",
        CubeColormap.Magma => "Magma",
        CubeColormap.Plasma => "Plasma",
        _ => c.ToString(),
    };

    /// <summary>
    /// Build the 256×1 RGBA8 LUT for a colormap (tightly packed, RGBA order). The
    /// perceptual maps (viridis/inferno/magma/plasma) use matplotlib anchor stops;
    /// grayscale/inverted/heat/cool are procedural (matching the 2D FITS viewer).
    /// </summary>
    public static byte[] Build(CubeColormap name) => name switch
    {
        CubeColormap.Grayscale => Procedural(static t => (t, t, t)),
        CubeColormap.Inverted => Procedural(static t => (1 - t, 1 - t, 1 - t)),
        CubeColormap.Heat => Procedural(static t => (
            Math.Clamp(t * 3f, 0, 1),
            Math.Clamp((t - 0.33f) * 3f, 0, 1),
            Math.Clamp((t - 0.67f) * 3f, 0, 1))),
        CubeColormap.Cool => Procedural(static t => (t, 1 - t, 1f)),
        CubeColormap.Viridis => Interpolate(ViridisStops),
        CubeColormap.Inferno => Interpolate(InfernoStops),
        CubeColormap.Magma => Interpolate(MagmaStops),
        CubeColormap.Plasma => Interpolate(PlasmaStops),
        _ => Interpolate(InfernoStops),
    };

    // Perceptual colormap anchor stops (RGB in 0..1), linearly interpolated to 256 entries.
    // Inferno/Viridis use accurate matplotlib anchors (higher fidelity than the macOS app's
    // 9-point inferno / polynomial viridis, matching the v-cube web viewer); Magma/Plasma use
    // the exact macOS cubeColormapStops 9-anchor tables (FITSRenderEngine.swift:199-208).

    // matplotlib inferno, 17 anchors (t = i/16).
    private static readonly (float r, float g, float b)[] InfernoStops =
    [
        (0.001462f, 0.000466f, 0.013866f), (0.046915f, 0.030324f, 0.150164f),
        (0.142378f, 0.046242f, 0.308553f), (0.258234f, 0.038571f, 0.406485f),
        (0.366529f, 0.071579f, 0.431994f), (0.472328f, 0.110547f, 0.428334f),
        (0.578304f, 0.148039f, 0.404411f), (0.682656f, 0.189501f, 0.360757f),
        (0.780517f, 0.243327f, 0.299523f), (0.865006f, 0.316822f, 0.226055f),
        (0.929644f, 0.411479f, 0.145367f), (0.970919f, 0.522853f, 0.058367f),
        (0.987622f, 0.645320f, 0.039886f), (0.978806f, 0.774545f, 0.176037f),
        (0.950018f, 0.903409f, 0.380271f), (0.954529f, 0.972590f, 0.612366f),
        (0.988362f, 0.998364f, 0.644924f),
    ];

    // matplotlib viridis, 11 anchors (t = i/10).
    private static readonly (float r, float g, float b)[] ViridisStops =
    [
        (0.267004f, 0.004874f, 0.329415f), (0.282623f, 0.140926f, 0.457517f),
        (0.253935f, 0.265254f, 0.529983f), (0.206756f, 0.371758f, 0.553117f),
        (0.163625f, 0.471133f, 0.558148f), (0.127568f, 0.566949f, 0.550556f),
        (0.134692f, 0.658636f, 0.517649f), (0.266941f, 0.748751f, 0.440573f),
        (0.477504f, 0.821444f, 0.318195f), (0.741388f, 0.873449f, 0.149561f),
        (0.993248f, 0.906157f, 0.143936f),
    ];

    // macOS magma, 9 anchors (verbatim from cubeColormapStops).
    private static readonly (float r, float g, float b)[] MagmaStops =
    [
        (0.001f, 0.000f, 0.014f), (0.078f, 0.043f, 0.206f), (0.232f, 0.059f, 0.438f),
        (0.390f, 0.100f, 0.502f), (0.550f, 0.161f, 0.506f), (0.716f, 0.215f, 0.475f),
        (0.868f, 0.288f, 0.409f), (0.967f, 0.440f, 0.360f), (0.987f, 0.991f, 0.749f),
    ];

    // macOS plasma, 9 anchors (verbatim from cubeColormapStops).
    private static readonly (float r, float g, float b)[] PlasmaStops =
    [
        (0.050f, 0.030f, 0.528f), (0.254f, 0.013f, 0.615f), (0.417f, 0.000f, 0.658f),
        (0.562f, 0.052f, 0.641f), (0.692f, 0.165f, 0.564f), (0.798f, 0.280f, 0.470f),
        (0.881f, 0.392f, 0.383f), (0.949f, 0.518f, 0.295f), (0.940f, 0.975f, 0.131f),
    ];

    private static byte[] Procedural(Func<float, (float r, float g, float b)> f)
    {
        var rgba = new byte[256 * 4];
        for (int i = 0; i < 256; i++)
        {
            var (r, g, b) = f(i / 255f);
            int o = i * 4;
            rgba[o + 0] = ToByte(r);
            rgba[o + 1] = ToByte(g);
            rgba[o + 2] = ToByte(b);
            rgba[o + 3] = 255;
        }
        return rgba;
    }

    private static byte[] Interpolate(ReadOnlySpan<(float r, float g, float b)> stops)
    {
        var rgba = new byte[256 * 4];
        int segCount = stops.Length - 1;
        for (int i = 0; i < 256; i++)
        {
            float t = i / 255f * segCount;
            int k = Math.Min((int)t, segCount - 1);
            float f = t - k;
            var a = stops[k];
            var b = stops[k + 1];
            int o = i * 4;
            rgba[o + 0] = ToByte(a.r + (b.r - a.r) * f);
            rgba[o + 1] = ToByte(a.g + (b.g - a.g) * f);
            rgba[o + 2] = ToByte(a.b + (b.b - a.b) * f);
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
