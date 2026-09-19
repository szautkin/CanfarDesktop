using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// The bounds on an agent capture, and the transform that makes one worth sending.
/// </summary>
public class AgentCaptureTests
{
    private const int Generous = 64 * 1024 * 1024; // a byte cap that never binds, so one limit is tested at a time

    // ── Size ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Never enlarged. Upscaling invents detail the data does not have — a 64x64 cutout blown up to
    /// 1024 looks like a 1024px observation of something, which is a lie about the data.
    /// </summary>
    [Fact]
    public void Fit_NeverEnlarges()
    {
        var size = AgentCapture.Fit(64, 64, maxPixels: 1024, maxBytes: Generous);

        Assert.Equal(64, size.Width);
        Assert.Equal(64, size.Height);
        Assert.Equal(1.0, size.Scale);
        Assert.False(size.WasReduced);
    }

    /// <summary>The cap is on the LONGEST edge, so a wide region stays wide.</summary>
    [Fact]
    public void Fit_CapsTheLongestEdge_AndKeepsTheAspect()
    {
        var size = AgentCapture.Fit(4000, 1000, maxPixels: 1000, maxBytes: Generous);

        Assert.Equal(1000, size.Width);
        Assert.Equal(250, size.Height);
        Assert.Equal(0.25, size.Scale, 6);
        Assert.NotNull(size.Note);
    }

    [Fact]
    public void Fit_TallRegion_IsCappedOnItsHeight()
    {
        var size = AgentCapture.Fit(500, 2000, maxPixels: 1000, maxBytes: Generous);

        Assert.Equal(250, size.Width);
        Assert.Equal(1000, size.Height);
    }

    /// <summary>The byte cap binds independently: a capture that arrives as an error is worse than a small one.</summary>
    [Fact]
    public void Fit_ByteCap_ReducesFurtherThanThePixelCap()
    {
        var generousPixels = AgentCapture.Fit(2000, 2000, maxPixels: 2000, maxBytes: Generous);
        var tightBytes = AgentCapture.Fit(2000, 2000, maxPixels: 2000, maxBytes: 64 * 1024);

        Assert.Equal(2000, generousPixels.Width);
        Assert.True(tightBytes.Width < generousPixels.Width);
        Assert.Contains("KB", tightBytes.Note);
    }

    /// <summary>Whichever limit bites harder wins; neither is allowed to undo the other.</summary>
    [Fact]
    public void Fit_BothCaps_TakesTheSmaller()
    {
        var size = AgentCapture.Fit(4000, 4000, maxPixels: 100, maxBytes: Generous);

        Assert.Equal(100, size.Width);
        Assert.Equal(100, size.Height);
    }

    /// <summary>A capture is never zero pixels wide, however hard the caps squeeze.</summary>
    [Fact]
    public void Fit_NeverReturnsAnEmptyImage()
    {
        var size = AgentCapture.Fit(10000, 10000, maxPixels: 1, maxBytes: 1);

        Assert.True(size.Width >= 1);
        Assert.True(size.Height >= 1);
    }

    [Fact]
    public void Fit_EmptyRegion_SaysSo()
    {
        var size = AgentCapture.Fit(0, 100, maxPixels: 1024, maxBytes: Generous);

        Assert.True(size.WasReduced);
        Assert.Equal("the region is empty", size.Note);
    }

    /// <summary>A nonsense or absent cap falls back to the default rather than to no limit.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Fit_NonPositiveCaps_FallBackToTheDefault(int maxPixels)
    {
        var size = AgentCapture.Fit(4000, 4000, maxPixels, maxBytes: Generous);
        Assert.Equal(AgentCapture.DefaultMaxPixels, size.Width);
    }

    /// <summary>The setting is clamped to a ceiling — past it the wire cap binds anyway.</summary>
    [Fact]
    public void Fit_MaxPixelsIsClampedToTheCeiling()
    {
        var size = AgentCapture.Fit(99999, 99999, maxPixels: 99999, maxBytes: Generous);
        Assert.Equal(AgentCapture.MaxPixelsCeiling, size.Width);
    }

    // ── The transform ────────────────────────────────────────────────────────

    /// <summary>
    /// The whole point of returning one: a position in the capture becomes an image pixel the other
    /// tools accept.
    /// </summary>
    [Fact]
    public void Transform_MapsACapturePositionToAnImagePixel()
    {
        // A 400x400 region starting at (100, 50), captured at 200x200 — half scale.
        var t = AgentCapture.TransformFor(100, 50, 0.5);

        Assert.Equal((100.0, 50.0), t.ToImage(0, 0));         // the capture's origin is the region's
        Assert.Equal((300.0, 250.0), t.ToImage(100, 100));    // halfway across is 200 image pixels in
        Assert.Equal((500.0, 450.0), t.ToImage(200, 200));    // the far corner
    }

    /// <summary>
    /// Paired with its inverse, exactly — the same discipline the pixel conventions keep, and for the
    /// same reason: an agent that points at a feature and reads the position back must get its own
    /// number.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(37.5, 912.25)]
    [InlineData(1023, 1023)]
    public void Transform_CancelsItsOwnInverse(double x, double y)
    {
        var t = AgentCapture.TransformFor(100, 50, 0.3125);

        var (ix, iy) = t.ToImage(x, y);
        var (bx, by) = t.ToCapture(ix, iy);

        Assert.Equal(x, bx, 9);
        Assert.Equal(y, by, 9);
    }

    /// <summary>A degenerate scale answers the origin rather than dividing by zero.</summary>
    [Fact]
    public void Transform_ZeroScale_DoesNotDivideByZero()
    {
        var t = AgentCapture.TransformFor(7, 9, 0);
        Assert.Equal((7.0, 9.0), t.ToImage(500, 500));
    }
}
