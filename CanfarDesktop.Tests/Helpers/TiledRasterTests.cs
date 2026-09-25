using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// Cutting a figure too large to rasterise in one go into pieces that fit.
///
/// A tiling that is off by a pixel leaves seams; one that overlaps produces a figure subtly wrong in
/// a way nobody notices until it is in a paper. So the properties here are exactness ones: the tiles
/// cover the figure, once each, and none of them is bigger than a single rasterisation allows.
/// </summary>
public class TiledRasterTests
{
    /// <summary>The case this was built for: the cube plate really reaching 4x.</summary>
    [Fact]
    public void TheCubePlateReachesFourTimesByTiling()
    {
        var plan = TiledRaster.For(1450, 1285, 4);

        Assert.Equal(5800, plan.Width);
        Assert.Equal(5140, plan.Height);
        Assert.Equal(4, plan.Scale, 9);
        Assert.True(plan.IsTiled, "5800 across cannot be one rasterisation");
    }

    /// <summary>A figure that already fits is one piece — no seams to risk for nothing.</summary>
    [Fact]
    public void AFigureThatFitsIsNotCutUp()
    {
        var plan = TiledRaster.For(1450, 1285, 2);

        Assert.Equal(2900, plan.Width);
        Assert.False(plan.IsTiled);
        Assert.Single(plan.Tiles);
    }

    /// <summary>Every piece must be something RenderTargetBitmap will actually produce.</summary>
    [Theory]
    [InlineData(1450, 1285, 4)]
    [InlineData(2000, 400, 4)]
    [InlineData(400, 2000, 4)]
    [InlineData(1000, 1000, 3.5)]
    public void NoTileExceedsWhatCanBeRasterised(double w, double h, double scale)
        => Assert.All(TiledRaster.For(w, h, scale).Tiles, t =>
        {
            Assert.InRange(t.Width, 1, RasterLimit.MaxEdge);
            Assert.InRange(t.Height, 1, RasterLimit.MaxEdge);
        });

    /// <summary>
    /// The tiles cover the whole figure and never each other. Checked by area and by walking the
    /// pixels of a small case, because "looks about right" is how a one-pixel seam ships.
    /// </summary>
    [Theory]
    [InlineData(1450, 1285, 4)]
    [InlineData(300, 200, 4)]
    [InlineData(1000, 1000, 4)]
    public void TheTilesCoverTheFigureExactlyOnce(double w, double h, double scale)
    {
        var plan = TiledRaster.For(w, h, scale);

        var covered = plan.Tiles.Sum(t => (long)t.Width * t.Height);
        Assert.Equal((long)plan.Width * plan.Height, covered);

        Assert.All(plan.Tiles, t =>
        {
            Assert.InRange(t.X, 0, plan.Width - 1);
            Assert.InRange(t.Y, 0, plan.Height - 1);
            Assert.True(t.X + t.Width <= plan.Width, "a tile runs off the right edge");
            Assert.True(t.Y + t.Height <= plan.Height, "a tile runs off the bottom edge");
        });
    }

    /// <summary>Walk a small tiling pixel by pixel: every pixel written once, none twice.</summary>
    [Fact]
    public void EveryPixelIsWrittenExactlyOnce()
    {
        var plan = TiledRaster.For(20, 15, 1, maxEdge: 7);
        var seen = new int[plan.Width, plan.Height];

        foreach (var t in plan.Tiles)
            for (var x = t.X; x < t.X + t.Width; x++)
            for (var y = t.Y; y < t.Y + t.Height; y++)
                seen[x, y]++;

        for (var x = 0; x < plan.Width; x++)
        for (var y = 0; y < plan.Height; y++)
            Assert.Equal(1, seen[x, y]);
    }

    /// <summary>
    /// Tiling lifts the per-rasterisation limit, not the cost of holding the result. A figure past
    /// what can be assembled is reduced — and says so by reporting the scale it really used.
    /// </summary>
    [Fact]
    public void AnUnassemblableFigureIsReducedRatherThanAttempted()
    {
        var plan = TiledRaster.For(8000, 8000, 4);

        Assert.True(plan.Scale < 4, $"scale stayed at {plan.Scale}");
        Assert.True((long)plan.Width * plan.Height <= TiledRaster.MaxTotalPixels);
    }

    [Theory]
    [InlineData(0, 100, 2)]
    [InlineData(100, 0, 2)]
    [InlineData(100, 100, 0)]
    [InlineData(double.NaN, 100, 2)]
    public void NothingToDrawIsAnEmptyPlan(double w, double h, double scale)
        => Assert.True(TiledRaster.For(w, h, scale).IsEmpty);
}
