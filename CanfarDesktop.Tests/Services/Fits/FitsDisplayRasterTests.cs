using Xunit;
using CanfarDesktop.Models.Fits;
using CanfarDesktop.Services.Fits;

namespace CanfarDesktop.Tests.Services.Fits;

/// <summary>
/// The viewer drew every image at full size into one bitmap: 1.6 GB for a MegaPipe tile, remade on
/// every slider move, and wider than a Direct3D 11 texture can be. Only the picture is reduced now;
/// the image's own pixels stay whole for values and coordinates.
/// </summary>
public class FitsDisplayRasterTests
{
    private static FitsImageData Image(int width, int height, Func<int, int, float> pixel, WcsInfo? wcs = null)
    {
        var pixels = new float[width * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                pixels[y * width + x] = pixel(x, y);
        return new FitsImageData { Pixels = pixels, Width = width, Height = height, Wcs = wcs, Unit = "ADU" };
    }

    private static float At(FitsImageData image, int x, int y) => image.Pixels[y * image.Width + x];

    // ── Which images are reduced ─────────────────────────────────────────────

    /// <summary>The mosaics that already opened keep opening exactly as they did.</summary>
    [Theory]
    [InlineData(64, 64)]
    [InlineData(4096, 4096)]
    [InlineData(11471, 4593)]
    public void ImagesWithinTheLimits_AreDrawnAtFullSize(int width, int height)
        => Assert.Equal(1.0, FitsDisplayRaster.ScaleFor(width, height));

    [Fact]
    public void AnImageThatFits_IsItsOwnPicture_NotACopy()
    {
        var image = Image(8, 8, (x, y) => x + y);
        Assert.Same(image, FitsDisplayRaster.For(image));
    }

    /// <summary>The tile that failed: 20315 × 20475, reduced to within both limits.</summary>
    [Fact]
    public void AMegaPipeTile_IsDrawnWithinBothLimits()
    {
        var scale = FitsDisplayRaster.ScaleFor(20315, 20475);
        var w = (int)(20315 * scale);
        var h = (int)(20475 * scale);

        Assert.InRange(scale, 0.40, 0.41); // √(64 M / 416 M): the area binds, not the side
        Assert.True((long)w * h <= FitsDisplayRaster.MaxPixels);
        Assert.True(Math.Max(w, h) <= FitsDisplayRaster.MaxSide);
    }

    /// <summary>A long strip is held to the texture's width even when its area would pass.</summary>
    [Fact]
    public void ALongStrip_IsHeldToTheWidestTexture()
    {
        var scale = FitsDisplayRaster.ScaleFor(40000, 100);
        Assert.Equal(FitsDisplayRaster.MaxSide, (int)Math.Round(40000 * scale));
    }

    // ── What the picture holds ───────────────────────────────────────────────

    [Fact]
    public void EachPicturePixel_IsTheMeanOfItsBlock()
    {
        // 4×4 into 2×2: each output pixel averages a 2×2 block.
        var image = Image(4, 4, (x, y) => y * 4 + x);

        var picture = FitsDisplayRaster.For(image, maxPixels: 4);

        Assert.Equal((2, 2), (picture.Width, picture.Height));
        Assert.Equal((0 + 1 + 4 + 5) / 4f, At(picture, 0, 0));
        Assert.Equal((2 + 3 + 6 + 7) / 4f, At(picture, 1, 0));
        Assert.Equal((8 + 9 + 12 + 13) / 4f, At(picture, 0, 1));
        Assert.Equal((10 + 11 + 14 + 15) / 4f, At(picture, 1, 1));
    }

    /// <summary>
    /// Why an average and not every Nth pixel: a star one pixel across, between the sampled pixels,
    /// would simply not be in the picture.
    /// </summary>
    [Fact]
    public void AOnePixelStar_StaysInThePicture()
    {
        var image = Image(6, 6, (x, y) => x == 4 && y == 1 ? 90f : 0f);

        var picture = FitsDisplayRaster.For(image, maxPixels: 4);

        Assert.True(picture.Max > 0, "the star was dropped");
        Assert.Equal(10f, At(picture, 1, 0)); // 90 spread over its 3×3 block
    }

    /// <summary>
    /// When the image does not divide evenly the blocks differ in size by one, and every pixel is
    /// counted exactly once: none dropped at an edge, none counted twice.
    /// </summary>
    [Fact]
    public void UnevenBlocks_CoverEveryPixelOnce()
    {
        var image = Image(5, 5, (_, _) => 1f);

        var picture = FitsDisplayRaster.For(image, maxPixels: 4);

        Assert.Equal((2, 2), (picture.Width, picture.Height));
        Assert.All(picture.Pixels, v => Assert.Equal(1f, v)); // a mean of ones, whatever the block size
    }

    [Fact]
    public void BlankPixels_AreLeftOutOfTheMean_AndAnAllBlankBlockStaysBlank()
    {
        var image = Image(4, 2, (x, y) => x < 2 ? float.NaN : (x == 2 && y == 0 ? float.NaN : 6f));

        var picture = FitsDisplayRaster.For(image, maxPixels: 2);

        Assert.True(float.IsNaN(At(picture, 0, 0)));
        Assert.Equal(6f, At(picture, 1, 0)); // three finite sixes, not (3 × 6) / 4
        Assert.Equal((6f, 6f), (picture.Min, picture.Max));
    }

    /// <summary>Stored bottom-up like the image, so the renderer's flip treats both alike.</summary>
    [Fact]
    public void RowsKeepTheImagesOrder()
    {
        var image = Image(2, 4, (_, y) => y);

        var picture = FitsDisplayRaster.For(image, maxPixels: 2);

        Assert.Equal((1, 2), (picture.Width, picture.Height));
        Assert.Equal(0.5f, At(picture, 0, 0)); // rows 0 and 1: the bottom of the image
        Assert.Equal(2.5f, At(picture, 0, 1));
    }

    /// <summary>
    /// Its pixels are not the image's, so it carries no WCS a coordinate could be wrongly read from.
    /// The unit is still the image's: the values are its values, averaged.
    /// </summary>
    [Fact]
    public void ThePicture_CarriesNoWcs_ButKeepsTheUnit()
    {
        var image = Image(4, 4, (_, _) => 1f, wcs: new WcsInfo());

        var picture = FitsDisplayRaster.For(image, maxPixels: 4);

        Assert.Null(picture.Wcs);
        Assert.Equal("ADU", picture.Unit);
    }
}
