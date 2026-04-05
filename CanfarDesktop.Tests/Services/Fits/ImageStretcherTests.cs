using Xunit;
using CanfarDesktop.Services.Fits;

namespace CanfarDesktop.Tests.Services.Fits;

public class ImageStretcherTests
{
    [Theory]
    [InlineData(0f, 0f, 100f, 0f)]    // min → 0
    [InlineData(100f, 0f, 100f, 1f)]   // max → 1
    [InlineData(50f, 0f, 100f, 0.5f)]  // middle → 0.5
    public void Linear_NormalizesCorrectly(float value, float min, float max, float expected)
    {
        var result = ImageStretcher.Stretch(value, min, max, ImageStretcher.StretchMode.Linear);
        Assert.Equal(expected, result, 3);
    }

    [Fact]
    public void Log_CompressesHighValues()
    {
        var low = ImageStretcher.Stretch(10f, 0f, 100f, ImageStretcher.StretchMode.Log);
        var high = ImageStretcher.Stretch(90f, 0f, 100f, ImageStretcher.StretchMode.Log);

        // Log stretch: low values get boosted, difference between high values compressed
        Assert.True(low > 0.1f); // linear would give 0.1
        Assert.True(high < 1.0f);
    }

    [Fact]
    public void Sqrt_CompressesHighValues()
    {
        var mid = ImageStretcher.Stretch(25f, 0f, 100f, ImageStretcher.StretchMode.Sqrt);
        // sqrt(0.25) = 0.5
        Assert.Equal(0.5f, mid, 2);
    }

    [Fact]
    public void Stretch_NaN_ReturnsZero()
    {
        var result = ImageStretcher.Stretch(float.NaN, 0, 100, ImageStretcher.StretchMode.Linear);
        Assert.Equal(0f, result);
    }

    [Fact]
    public void Stretch_EqualMinMax_Returns05()
    {
        var result = ImageStretcher.Stretch(5f, 5f, 5f, ImageStretcher.StretchMode.Linear);
        Assert.Equal(0.5f, result);
    }

    [Fact]
    public void StretchArray_SameLength()
    {
        var pixels = new float[] { 0, 25, 50, 75, 100 };
        var result = ImageStretcher.StretchArray(pixels, 0, 100, ImageStretcher.StretchMode.Linear);

        Assert.Equal(5, result.Length);
        Assert.Equal(0f, result[0], 3);
        Assert.Equal(1f, result[4], 3);
    }
}
