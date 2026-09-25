using Xunit;
using CanfarDesktop.Services.CubeViewer;
using CanfarDesktop.Services.Fits;

namespace CanfarDesktop.Tests.Services.Fits;

/// <summary>
/// A colormap is what its name says, and the same in both viewers.
///
/// <para>The FITS viewer's "Viridis" was a hand-written approximation running teal-blue to orange;
/// the cube viewer's used matplotlib's stops, purple to yellow. The same name meant two different
/// maps, and figures exported from the FITS viewer were labelled VIRIDIS in colours that were not.</para>
/// </summary>
public class ColormapConsistencyTests
{
    public static TheoryData<ColormapProvider.ColormapName, CubeColormap> SharedNames => new()
    {
        { ColormapProvider.ColormapName.Grayscale, CubeColormap.Grayscale },
        { ColormapProvider.ColormapName.Inverted, CubeColormap.Inverted },
        { ColormapProvider.ColormapName.Heat, CubeColormap.Heat },
        { ColormapProvider.ColormapName.Cool, CubeColormap.Cool },
        { ColormapProvider.ColormapName.Viridis, CubeColormap.Viridis },
    };

    [Theory]
    [MemberData(nameof(SharedNames))]
    public void BothViewersShowTheSameColoursForTheSameName(ColormapProvider.ColormapName fits, CubeColormap cube)
    {
        var fitsLut = ColormapProvider.GetColormap(fits);
        var cubeLut = CubeColormaps.Build(cube);

        for (var i = 0; i < 256; i++)
        {
            Assert.Equal(cubeLut[i * 4 + 0], fitsLut[i].R);
            Assert.Equal(cubeLut[i * 4 + 1], fitsLut[i].G);
            Assert.Equal(cubeLut[i * 4 + 2], fitsLut[i].B);
        }
    }

    /// <summary>matplotlib's viridis: #440154 at the bottom, #21908D in the middle, #FDE725 at the top.</summary>
    [Theory]
    [InlineData(0, 0x44, 0x01, 0x54)]
    [InlineData(128, 0x21, 0x90, 0x8D)]
    [InlineData(255, 0xFD, 0xE7, 0x25)]
    public void ViridisIsViridis(int index, int r, int g, int b)
    {
        var c = ColormapProvider.GetColormap(ColormapProvider.ColormapName.Viridis)[index];

        Assert.InRange(c.R, r - 2, r + 2);
        Assert.InRange(c.G, g - 2, g + 2);
        Assert.InRange(c.B, b - 2, b + 2);
    }
}
