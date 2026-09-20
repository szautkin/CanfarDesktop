using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// What a figure export can actually produce.
///
/// <para>RenderTargetBitmap will not exceed its longest-edge limit, and it does not fail when asked
/// for more — it quietly renders smaller, keeping the aspect. A plate 1450 across asked for 4x came
/// back at 4096, not 5800: bigger than the 2x export, but nothing like twice it. The app offered
/// "2x or 4x" and delivered 2x and 2.8x, which is how it was reported — "I see no difference".</para>
///
/// <para>The limit cannot be removed without tiling. It can be told the truth about.</para>
/// </summary>
public class RasterLimitTests
{
    /// <summary>The measured case, from the cube plate this was found on.</summary>
    [Fact]
    public void TheCubePlateCannotActuallyReachFourTimes()
    {
        var at2 = RasterLimit.Fit(1450, 1285, 2);
        var at4 = RasterLimit.Fit(1450, 1285, 4);

        Assert.False(at2.Clamped);
        Assert.Equal(2900, at2.Width);

        Assert.True(at4.Clamped);
        Assert.Equal(RasterLimit.MaxEdge, at4.Width);
        Assert.True(at4.Scale < 3, $"4x really delivers {at4.Scale:0.##}x");
    }

    /// <summary>A request that fits is left exactly alone.</summary>
    [Theory]
    [InlineData(800, 600, 2)]
    [InlineData(1000, 500, 4)]
    [InlineData(4096, 4096, 1)]
    public void ARequestThatFitsIsNotChanged(double w, double h, int scale)
    {
        var fit = RasterLimit.Fit(w, h, scale);

        Assert.False(fit.Clamped);
        Assert.Equal(scale, fit.Scale, 9);
        Assert.Equal((int)(w * scale), fit.Width);
        Assert.Equal((int)(h * scale), fit.Height);
    }

    /// <summary>Whatever is asked for, the result is something that will actually rasterise.</summary>
    [Theory]
    [InlineData(1450, 1285, 4)]
    [InlineData(4000, 300, 4)]
    [InlineData(300, 4000, 4)]
    [InlineData(9000, 9000, 2)]
    public void TheResultNeverExceedsTheLimit(double w, double h, int scale)
    {
        var fit = RasterLimit.Fit(w, h, scale);

        Assert.True(Math.Max(fit.Width, fit.Height) <= RasterLimit.MaxEdge,
            $"{fit.Width}x{fit.Height} is over the limit");
    }

    /// <summary>Clamping keeps the picture's shape — a squashed figure would be worse than a small one.</summary>
    [Fact]
    public void ClampingKeepsTheAspect()
    {
        var fit = RasterLimit.Fit(1450, 1285, 4);

        Assert.Equal(1450.0 / 1285.0, (double)fit.Width / fit.Height, 2);
    }

    /// <summary>
    /// A plate already past the limit still has to produce something. A smaller figure beats none.
    /// </summary>
    [Fact]
    public void APlateBiggerThanTheLimitStillRenders()
    {
        var fit = RasterLimit.Fit(9000, 6000, 1);

        Assert.True(fit.Clamped);
        Assert.True(fit.Scale < 1);
        Assert.Equal(RasterLimit.MaxEdge, fit.Width);
    }

    /// <summary>Nothing to scale is not a figure.</summary>
    [Theory]
    [InlineData(0, 100, 2)]
    [InlineData(100, 0, 2)]
    [InlineData(100, 100, 0)]
    [InlineData(double.NaN, 100, 2)]
    public void NoMeasurableSizeYieldsNothing(double w, double h, double scale)
        => Assert.Equal(0, RasterLimit.Fit(w, h, scale).Width);

    /// <summary>What an interface could honestly offer for a given plate.</summary>
    [Fact]
    public void TheLargestUsefulScaleIsTheOneThatJustFits()
    {
        var largest = RasterLimit.LargestUsefulScale(1450, 1285);

        Assert.False(RasterLimit.Fit(1450, 1285, largest).Clamped);
        Assert.True(RasterLimit.Fit(1450, 1285, largest * 1.01).Clamped);
    }
}
