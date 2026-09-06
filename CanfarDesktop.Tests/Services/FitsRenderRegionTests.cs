using Xunit;
using CanfarDesktop.Models.Fits;
using CanfarDesktop.Services.Fits;

namespace CanfarDesktop.Tests.Services;

/// <summary>
/// Rendering one region of an image at an arbitrary size — what an exported figure is made of.
///
/// Rendered from the pixel data through a substituted geometry, not captured from the screen: a
/// screenshot carries the zoom someone left the viewer at, a crosshair, and the screen's own
/// resampling. These tests are about the picture being of the DATA.
/// </summary>
public class FitsRenderRegionTests
{
    private const int Width = 8, Height = 8;

    /// <summary>
    /// An image whose every pixel encodes its own position, in the FITS convention: row 0 at the BOTTOM.
    /// Pixel value = fitsRow * 8 + column, so a rendered pixel can be traced back to where it came from.
    /// </summary>
    private static FitsImageData Ramp()
    {
        var pixels = new float[Width * Height];
        for (var row = 0; row < Height; row++)
            for (var col = 0; col < Width; col++)
                pixels[row * Width + col] = row * Width + col;

        return new FitsImageData { Pixels = pixels, Width = Width, Height = Height, Min = 0, Max = 63 };
    }

    /// <summary>A greyscale ramp, so a rendered byte reads straight back as the value that produced it.</summary>
    private static Windows.UI.Color[] Greys()
        => Enumerable.Range(0, 256).Select(i => Windows.UI.Color.FromArgb(255, (byte)i, (byte)i, (byte)i)).ToArray();

    private static byte[]? Render(FitsRegion region, int outW, int outH)
        => FitsRenderer.RenderRegion(Ramp(), region, outW, outH,
            ImageStretcher.StretchMode.Linear, Greys(), 0, 63);

    /// <summary>The blue channel of an output pixel — the colormap is grey, so all three agree.</summary>
    private static byte At(byte[] bgra, int outW, int x, int y) => bgra[(y * outW + x) * 4];

    /// <summary>
    /// The byte a source VALUE renders to: linearly stretched over 0..63 and used as a LUT index.
    /// Written out here rather than obtained from the renderer, so the test checks the mapping instead
    /// of agreeing with it.
    /// </summary>
    private static byte Rendered(float value) => (byte)Math.Clamp((int)(value / 63f * 255), 0, 255);

    [Fact]
    public void AWholeImageRendersEveryPixelOnce()
    {
        var bgra = Render(FitsRegion.WholeImage(Width, Height), Width, Height);
        Assert.NotNull(bgra);

        // Display row 0 is the TOP, which is FITS row 7 — values 56..63.
        Assert.Equal(Rendered(56), At(bgra!, Width, 0, 0));
        Assert.Equal(Rendered(63), At(bgra!, Width, 7, 0));

        // Display row 7 is the bottom, which is FITS row 0 — values 0..7.
        Assert.Equal(Rendered(0), At(bgra!, Width, 0, 7));
        Assert.Equal(Rendered(7), At(bgra!, Width, 7, 7));
    }

    /// <summary>A region is a crop: the output starts at the region's corner, not the image's.</summary>
    [Fact]
    public void ARegionRendersOnlyItself()
    {
        var bgra = Render(new FitsRegion(4, 0, 4, 4), outW: 4, outH: 4);
        Assert.NotNull(bgra);

        // Display (4,0) is FITS row 7, column 4 → 60.
        Assert.Equal(Rendered(60), At(bgra!, 4, 0, 0));
        Assert.Equal(Rendered(63), At(bgra!, 4, 3, 0));
    }

    /// <summary>
    /// Scaling up repeats pixels rather than blending them. Smoothing a 4x export would invent values
    /// between the pixels, and where a point source IS one pixel that is a claim the data does not make.
    /// </summary>
    [Fact]
    public void ScalingUpRepeatsPixelsRatherThanBlendingThem()
    {
        var bgra = Render(new FitsRegion(0, 0, 2, 2), outW: 8, outH: 8);
        Assert.NotNull(bgra);

        // Each source pixel becomes a 4x4 block of exactly its own value — no intermediate greys.
        var topLeft = At(bgra!, 8, 0, 0);
        foreach (var (x, y) in new[] { (0, 0), (3, 0), (0, 3), (3, 3) })
            Assert.Equal(topLeft, At(bgra!, 8, x, y));

        var topRight = At(bgra!, 8, 4, 0);
        Assert.NotEqual(topLeft, topRight);
        Assert.Equal(topRight, At(bgra!, 8, 7, 3));
    }

    /// <summary>
    /// Each output pixel samples the MIDDLE of its footprint. Sampling the corner shifts the whole
    /// picture half an output pixel, which at 4x is visible against an overlay drawn from the same
    /// coordinates.
    /// </summary>
    [Fact]
    public void ScalingDownSamplesTheMiddleOfEachFootprint()
    {
        var bgra = Render(FitsRegion.WholeImage(Width, Height), outW: 4, outH: 4);
        Assert.NotNull(bgra);

        // Output column 0 covers display x 0..2, so it samples x=1; output row 0 covers display y 0..2,
        // sampling y=1 → FITS row 6, column 1 → 49.
        Assert.Equal(Rendered(49), At(bgra!, 4, 0, 0));
    }

    [Fact]
    public void ARegionOffTheImageRendersNothingRatherThanASliver()
        => Assert.Null(Render(new FitsRegion(100, 100, 10, 10), 10, 10));

    [Fact]
    public void ARegionPartlyOffTheImageRendersWhatOverlaps()
    {
        var bgra = Render(new FitsRegion(-4, -4, 8, 8), outW: 4, outH: 4);

        // The clamped region is the image's own top-left 4x4, so it renders rather than failing.
        Assert.NotNull(bgra);
        Assert.Equal(4 * 4 * 4, bgra!.Length);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(-1, 10)]
    public void AnOutputWithNoAreaIsRefused(int outW, int outH)
        => Assert.Null(Render(FitsRegion.WholeImage(Width, Height), outW, outH));

    [Fact]
    public void EveryRenderedPixelIsOpaque()
    {
        var bgra = Render(FitsRegion.WholeImage(Width, Height), Width, Height)!;

        for (var i = 3; i < bgra.Length; i += 4)
            Assert.Equal(255, bgra[i]);
    }
}
