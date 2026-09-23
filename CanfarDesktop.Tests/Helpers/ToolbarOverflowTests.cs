using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// A toolbar wider than its window.
///
/// <para>The FITS toolbar carries more controls than a narrow window can show, and before it scrolled
/// the last of them simply sat off the end with nothing to say they existed. These are the rules for
/// when the step buttons appear and where a step lands — three numbers in, one out, no window.</para>
/// </summary>
public class ToolbarOverflowTests
{
    // A 400-wide window onto a 1000-wide strip: 600 of it is out of sight.
    private const double Viewport = 400, Extent = 1000;

    // ── Is anything out of sight ────────────────────────────────────────────────────────────────

    [Fact]
    public void AStripThatFitsHasNoOverflow()
        => Assert.False(ToolbarOverflow.HasOverflow(viewport: 1000, extent: 800));

    [Fact]
    public void AStripWiderThanItsWindowOverflows()
        => Assert.True(ToolbarOverflow.HasOverflow(Viewport, Extent));

    /// <summary>
    /// Half a pixel of difference is layout rounding, not a control hidden off the end. Showing a step
    /// button for it would offer a scroll that goes nowhere.
    /// </summary>
    [Fact]
    public void RoundingIsNotOverflow()
        => Assert.False(ToolbarOverflow.HasOverflow(viewport: 400, extent: 400.3));

    [Fact]
    public void TheFurthestScrollIsTheHiddenWidth()
        => Assert.Equal(600, ToolbarOverflow.MaxOffset(Viewport, Extent));

    [Theory]
    [InlineData(double.NaN, 1000)]
    [InlineData(400, double.PositiveInfinity)]
    [InlineData(-50, 1000)]
    public void UnusableMeasurementsNeverProduceANegativeOrInfiniteRange(double viewport, double extent)
    {
        var max = ToolbarOverflow.MaxOffset(viewport, extent);
        Assert.True(double.IsFinite(max) && max >= 0);
    }

    // ── Which way out there is ──────────────────────────────────────────────────────────────────

    /// <summary>At the start there is nothing behind you, so only the forward button shows.</summary>
    [Fact]
    public void AtTheStartOnlyForwardIsOffered()
    {
        Assert.False(ToolbarOverflow.CanScrollBack(0, Viewport, Extent));
        Assert.True(ToolbarOverflow.CanScrollForward(0, Viewport, Extent));
    }

    /// <summary>At the end, the reverse — a forward button there would do nothing when pressed.</summary>
    [Fact]
    public void AtTheEndOnlyBackIsOffered()
    {
        Assert.True(ToolbarOverflow.CanScrollBack(600, Viewport, Extent));
        Assert.False(ToolbarOverflow.CanScrollForward(600, Viewport, Extent));
    }

    [Fact]
    public void InTheMiddleBothAreOffered()
    {
        Assert.True(ToolbarOverflow.CanScrollBack(300, Viewport, Extent));
        Assert.True(ToolbarOverflow.CanScrollForward(300, Viewport, Extent));
    }

    /// <summary>A strip that fits offers neither, however it has been scrolled.</summary>
    [Fact]
    public void AStripThatFitsOffersNeither()
    {
        Assert.False(ToolbarOverflow.CanScrollBack(50, viewport: 1000, extent: 800));
        Assert.False(ToolbarOverflow.CanScrollForward(50, viewport: 1000, extent: 800));
    }

    // ── Where a step lands ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A step keeps a little of the old view, so a control sitting on the seam is not stepped clean
    /// over — a full viewport would leave no landmark from the screen before.
    /// </summary>
    [Fact]
    public void AStepLeavesAnOverlap()
        => Assert.Equal(Viewport - ToolbarOverflow.Overlap, ToolbarOverflow.Forward(0, Viewport, Extent));

    [Fact]
    public void BackUndoesForwardAwayFromTheEnds()
    {
        var forward = ToolbarOverflow.Forward(100, Viewport, Extent);
        Assert.Equal(100, ToolbarOverflow.Back(forward, Viewport, Extent));
    }

    /// <summary>A step never carries the strip past either end.</summary>
    [Fact]
    public void StepsStopAtTheEnds()
    {
        Assert.Equal(600, ToolbarOverflow.Forward(550, Viewport, Extent));
        Assert.Equal(0, ToolbarOverflow.Back(50, Viewport, Extent));
    }

    /// <summary>
    /// On a very narrow strip the overlap would eat the whole step, and a button that moves nothing is
    /// worse than no button — so the step becomes half the viewport instead.
    /// </summary>
    [Fact]
    public void ANarrowStripStillMakesProgress()
    {
        var step = ToolbarOverflow.Forward(0, viewport: 60, extent: 1000);

        Assert.True(step > 0);
        Assert.Equal(30, step);
    }

    [Fact]
    public void AnOffsetOutsideTheRangeIsClampedIntoIt()
    {
        Assert.Equal(600, ToolbarOverflow.Clamp(5000, Viewport, Extent));
        Assert.Equal(0, ToolbarOverflow.Clamp(-20, Viewport, Extent));
        Assert.Equal(0, ToolbarOverflow.Clamp(double.NaN, Viewport, Extent));
    }
}
