using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// How far a floating panel travels to get out of the way.
///
/// The cube's panels sit over the render and swallow the picture on a narrow window. Hiding them
/// outright takes their existence with them — nothing says the controls are still there. Parking each
/// against its nearest edge with a sliver showing says both things at once.
/// </summary>
public class PanelSlideTests
{
    /// <summary>The panel crosses its own width AND the gap it keeps from the edge, less the sliver.</summary>
    [Theory]
    [InlineData(264, 20, 279)]      // the control column
    [InlineData(320, 20, 335)]      // the info panel
    [InlineData(100, 0, 95)]        // flush against the edge already
    public void APanelTravelsItsOwnSizePlusItsMargin(double extent, double margin, double expected)
        => Assert.Equal(expected, PanelSlide.Offset(extent, margin), 6);

    /// <summary>Exactly the sliver is left behind, wherever it started.</summary>
    [Theory]
    [InlineData(264, 20)]
    [InlineData(40, 8)]
    [InlineData(1000, 64)]
    public void TheSliverIsWhatRemains(double extent, double margin)
    {
        var travelled = PanelSlide.Offset(extent, margin);

        // It started at `margin` from the edge and occupied `extent`; after travelling, its far side
        // sits exactly Peek inside the edge.
        Assert.Equal(PanelSlide.Peek, margin + extent - travelled, 6);
    }

    /// <summary>A distance, never a direction — which edge it heads for is the caller's business.</summary>
    [Fact]
    public void TheOffsetIsNeverNegative()
        => Assert.All(
            new[] { (1.0, 0.0), (3.0, 0.0), (5.0, 0.0), (0.5, 0.5) },
            pair => Assert.True(PanelSlide.Offset(pair.Item1, pair.Item2) >= 0));

    /// <summary>
    /// A panel no bigger than the sliver is already smaller than the reminder would be, so moving it
    /// gains nothing.
    /// </summary>
    [Theory]
    [InlineData(4, 0)]
    [InlineData(5, 0)]
    public void APanelSmallerThanTheSliverStaysPut(double extent, double margin)
        => Assert.Equal(0, PanelSlide.Offset(extent, margin), 6);

    /// <summary>
    /// Nothing measurable, nowhere to go. A collapsed panel reports no size, and moving it by a
    /// guess would put it back in the wrong place when it reappears.
    /// </summary>
    [Theory]
    [InlineData(0, 20)]
    [InlineData(-10, 20)]
    [InlineData(double.NaN, 20)]
    [InlineData(double.PositiveInfinity, 20)]
    public void NoMeasurableSizeMeansNoMovement(double extent, double margin)
        => Assert.Equal(0, PanelSlide.Offset(extent, margin), 6);

    /// <summary>A nonsense margin is ignored rather than dragging the panel somewhere absurd.</summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-50)]
    public void AnUnusableMarginIsTreatedAsNone(double margin)
        => Assert.Equal(PanelSlide.Offset(264, 0), PanelSlide.Offset(264, margin), 6);
}
