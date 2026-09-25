using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// The factor a figure's marks are inked at.
///
/// A mark's stroke is in device pixels, and a figure is not the screen — a cube volume export is a
/// fixed 1400px picture whatever size the viewport was. Left at 1.0 the marks would be the one thing
/// in the figure drawn at the old size, which is the mistake the FITS plate's InkScale already exists
/// to avoid.
/// </summary>
public class PlateInkTests
{
    /// <summary>The ordinary case: a 1400px export off a 700px viewport is drawn at 2×.</summary>
    [Theory]
    [InlineData(1400, 700, 2.0)]
    [InlineData(1400, 1400, 1.0)]
    [InlineData(1600, 800, 2.0)]
    [InlineData(900, 1200, 0.75)]     // a figure smaller than the viewport thins its ink to match
    public void TheFactorIsTheRatioOfThePictureToTheScreen(double frame, double screen, double want)
        => Assert.Equal(want, PlateInk.ScaleFor(frame, screen), 6);

    /// <summary>
    /// A viewport of a few pixels — a window mid-resize, a page not yet laid out — would otherwise
    /// give a factor in the hundreds and strokes that swallow the figure.
    /// </summary>
    [Theory]
    [InlineData(1400, 4)]
    [InlineData(1400, 1)]
    [InlineData(4096, 10)]
    public void AnAbsurdlySmallViewportIsClampedAtTheTop(double frame, double screen)
        => Assert.Equal(PlateInk.Max, PlateInk.ScaleFor(frame, screen), 6);

    [Theory]
    [InlineData(10, 4000)]
    [InlineData(1, 1400)]
    public void AnAbsurdlySmallPictureIsClampedAtTheBottom(double frame, double screen)
        => Assert.Equal(PlateInk.Min, PlateInk.ScaleFor(frame, screen), 6);

    /// <summary>
    /// Not a measurement, so not a factor. One is the honest answer: draw the marks at the weight they
    /// have on screen rather than guessing, which is what every caller did before this existed.
    /// </summary>
    [Theory]
    [InlineData(0, 700)]
    [InlineData(1400, 0)]
    [InlineData(-1400, 700)]
    [InlineData(1400, -700)]
    [InlineData(double.NaN, 700)]
    [InlineData(1400, double.NaN)]
    [InlineData(double.PositiveInfinity, 700)]
    [InlineData(1400, double.PositiveInfinity)]
    public void AnUnusableMeasurementMeansTheScreensOwnInk(double frame, double screen)
        => Assert.Equal(1.0, PlateInk.ScaleFor(frame, screen), 6);

    /// <summary>The result is always something a stroke can be multiplied by.</summary>
    [Theory]
    [InlineData(1400, 700)]
    [InlineData(1400, 0.0001)]
    [InlineData(0.0001, 1400)]
    [InlineData(0, 0)]
    public void TheFactorIsAlwaysFiniteAndPositive(double frame, double screen)
    {
        var scale = PlateInk.ScaleFor(frame, screen);

        Assert.True(double.IsFinite(scale) && scale > 0, $"got {scale}");
        Assert.InRange(scale, PlateInk.Min, PlateInk.Max);
    }
}
