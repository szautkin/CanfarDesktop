using Xunit;
using CanfarDesktop.Models.Fits;
using CanfarDesktop.Services.Fits;

namespace CanfarDesktop.Tests.Services.Fits;

public class FitsRendererTests
{
    private static FitsImageData CreateTestImage(int width, int height, float fillValue = 0.5f)
    {
        var pixels = new float[width * height];
        Array.Fill(pixels, fillValue);
        return new FitsImageData
        {
            Pixels = pixels,
            Width = width,
            Height = height,
            Min = 0,
            Max = 1,
        };
    }

    [Fact]
    public void Render_CorrectOutputSize()
    {
        var image = CreateTestImage(100, 50);
        var colormap = ColormapProvider.GetColormap(ColormapProvider.ColormapName.Grayscale);

        var bgra = FitsRenderer.Render(image, ImageStretcher.StretchMode.Linear, colormap, 0, 1);

        Assert.Equal(100 * 50 * 4, bgra.Length);
    }

    [Fact]
    public void Render_GrayscaleMidValue_IsGray()
    {
        var image = CreateTestImage(1, 1, 0.5f);
        var colormap = ColormapProvider.GetColormap(ColormapProvider.ColormapName.Grayscale);

        var bgra = FitsRenderer.Render(image, ImageStretcher.StretchMode.Linear, colormap, 0, 1);

        // Mid gray = ~127-128
        Assert.InRange(bgra[0], 120, 135); // B
        Assert.InRange(bgra[1], 120, 135); // G
        Assert.InRange(bgra[2], 120, 135); // R
        Assert.Equal(255, bgra[3]);        // A
    }

    [Fact]
    public void Render_FlipsYAxis()
    {
        // 2x2 image: bottom-left=0, bottom-right=0.5, top-left=1, top-right=1
        // FITS row 0 (bottom) = [0, 0.5], FITS row 1 (top) = [1, 1]
        var pixels = new float[] { 0f, 0.5f, 1f, 1f };
        var image = new FitsImageData { Pixels = pixels, Width = 2, Height = 2, Min = 0, Max = 1 };
        var colormap = ColormapProvider.GetColormap(ColormapProvider.ColormapName.Grayscale);

        var bgra = FitsRenderer.Render(image, ImageStretcher.StretchMode.Linear, colormap, 0, 1);

        // Display row 0 (top) should be FITS row 1 (top) = bright
        Assert.True(bgra[0] > 200); // top-left pixel blue channel = bright
        // Display row 1 (bottom) should be FITS row 0 (bottom) = dark
        Assert.True(bgra[2 * 4] < 10); // bottom-left pixel = dark
    }

    [Fact]
    public void AutoCut_ReturnsReasonableRange()
    {
        var rng = new Random(42);
        var pixels = new float[10000];
        for (var i = 0; i < pixels.Length; i++)
            pixels[i] = (float)(rng.NextDouble() * 100);

        var image = new FitsImageData { Pixels = pixels, Width = 100, Height = 100, Min = 0, Max = 100 };
        var (min, max) = FitsRenderer.AutoCut(image);

        Assert.True(min >= 0);
        Assert.True(max <= 100);
        Assert.True(min < max);
        // With 0.5%/99.5% percentile on uniform, expect ~0.5 to ~99.5
        Assert.InRange(min, 0, 5);
        Assert.InRange(max, 95, 100);
    }

    [Fact]
    public void Colormap_Grayscale_256Entries()
    {
        var lut = ColormapProvider.GetColormap(ColormapProvider.ColormapName.Grayscale);
        Assert.Equal(256, lut.Length);
        Assert.Equal(0, lut[0].R);   // Black
        Assert.Equal(255, lut[255].R); // White
    }

    [Fact]
    public void Colormap_Heat_256Entries()
    {
        var lut = ColormapProvider.GetColormap(ColormapProvider.ColormapName.Heat);
        Assert.Equal(256, lut.Length);
        Assert.Equal(0, lut[0].R);     // Dark
        Assert.Equal(255, lut[255].R); // Full red
    }
}
