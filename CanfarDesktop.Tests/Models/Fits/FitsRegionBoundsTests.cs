using Xunit;
using CanfarDesktop.Models.Fits;

namespace CanfarDesktop.Tests.Models.Fits;

/// <summary>
/// "Is this display pixel on that rectangle, and if not, where is the nearest point that is?"
///
/// Three places ask it — the export plate of its region, the canvas of the whole image, and the canvas
/// again when it decides where a press or a drag puts a mark. They have to agree: an answer that
/// differed between them would mean a mark that shows on screen and not in the figure, or a mark placed
/// somewhere it will never be drawn.
/// </summary>
public class FitsRegionBoundsTests
{
    private static FitsRegion Frame() => FitsRegion.WholeImage(200, 100);

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(100.0, 50.0)]
    [InlineData(199.0, 99.0)]
    public void APointInsideIsInside(double x, double y)
        => Assert.True(Frame().Contains(x, y));

    /// <summary>
    /// The edge belongs to the rectangle. Inclusive on all four sides, because the border is where a
    /// mark is hardest to notice going missing.
    /// </summary>
    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(200.0, 0.0)]
    [InlineData(0.0, 100.0)]
    [InlineData(200.0, 100.0)]
    public void EveryCornerIsInside(double x, double y)
        => Assert.True(Frame().Contains(x, y));

    [Theory]
    [InlineData(-0.5, 50.0)]
    [InlineData(200.5, 50.0)]
    [InlineData(100.0, -0.5)]
    [InlineData(100.0, 100.5)]
    [InlineData(-4000.0, -4000.0)]
    public void APointOutsideIsOutside(double x, double y)
        => Assert.False(Frame().Contains(x, y));

    /// <summary>
    /// Not a number is not a position. The comparisons alone would reject it — every comparison
    /// against NaN is false — but only by accident, and the reverse test ("is it outside?") written
    /// the obvious way would then accept it. The finiteness check says which answer is intended.
    /// </summary>
    [Theory]
    [InlineData(double.NaN, 50.0)]
    [InlineData(50.0, double.NaN)]
    [InlineData(double.PositiveInfinity, 50.0)]
    public void ANonFinitePointIsNotInside(double x, double y)
        => Assert.False(Frame().Contains(x, y));

    /// <summary>A region offset from the origin — a plate's region is not the whole image.</summary>
    [Fact]
    public void AnOffsetRegionIsMeasuredFromItsOwnOrigin()
    {
        var region = new FitsRegion(50, 20, 100, 40);

        Assert.True(region.Contains(50, 20));
        Assert.True(region.Contains(150, 60));
        Assert.False(region.Contains(49, 40));
        Assert.False(region.Contains(151, 40));
    }

    // ── Clamp ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>Clamping something already inside must not move it.</summary>
    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(123.25, 7.5)]
    [InlineData(200.0, 100.0)]
    public void APointInsideIsLeftWhereItIs(double x, double y)
        => Assert.Equal((x, y), Frame().Clamp(x, y));

    [Theory]
    [InlineData(-10.0, 50.0, 0.0, 50.0)]
    [InlineData(9999.0, 50.0, 200.0, 50.0)]
    [InlineData(100.0, -10.0, 100.0, 0.0)]
    [InlineData(100.0, 9999.0, 100.0, 100.0)]
    [InlineData(-10.0, 9999.0, 0.0, 100.0)]
    public void APointOutsideComesBackToTheNearestEdge(double x, double y, double wantX, double wantY)
        => Assert.Equal((wantX, wantY), Frame().Clamp(x, y));

    /// <summary>
    /// The two are companions, and this is the property that makes them one rule rather than two:
    /// whatever Clamp returns, Contains accepts.
    /// </summary>
    [Theory]
    [InlineData(-10.0, 50.0)]
    [InlineData(9999.0, -9999.0)]
    [InlineData(100.0, 50.0)]
    [InlineData(200.0, 100.0)]
    public void WhateverClampReturnsIsInside(double x, double y)
    {
        var (cx, cy) = Frame().Clamp(x, y);

        Assert.True(Frame().Contains(cx, cy), $"({x}, {y}) clamped to ({cx}, {cy})");
    }
}
