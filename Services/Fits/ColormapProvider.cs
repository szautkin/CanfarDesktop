namespace CanfarDesktop.Services.Fits;

using CanfarDesktop.Services.CubeViewer;
using Windows.UI;

/// <summary>
/// The FITS viewer's 256-entry colour lookup tables.
///
/// <para>Built from the cube viewer's tables, so a colormap is the same colours in both viewers.
/// This file used to generate its own; its "Viridis" was a hand-written approximation running
/// teal-blue to orange, where the cube viewer used matplotlib's stops, purple to yellow — and FITS
/// figures went out labelled VIRIDIS in colours that were not.</para>
/// </summary>
public static class ColormapProvider
{
    public enum ColormapName { Grayscale, Inverted, Heat, Cool, Viridis }

    public static Color[] GetColormap(ColormapName name)
    {
        var rgba = CubeColormaps.Build(name switch
        {
            ColormapName.Inverted => CubeColormap.Inverted,
            ColormapName.Heat => CubeColormap.Heat,
            ColormapName.Cool => CubeColormap.Cool,
            ColormapName.Viridis => CubeColormap.Viridis,
            _ => CubeColormap.Grayscale,
        });

        var lut = new Color[256];
        for (var i = 0; i < lut.Length; i++)
            lut[i] = Color.FromArgb(255, rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2]);
        return lut;
    }
}
