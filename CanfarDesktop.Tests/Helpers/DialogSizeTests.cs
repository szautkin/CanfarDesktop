using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// How big a dialog may be, given the window it has to fit inside.
///
/// The export dialogs asked for a fixed 1000x600. A ContentDialog does not shrink content it cannot
/// fit — it clips it — and what got clipped was the bottom of the options column, where the export
/// buttons were. Scrolling could not reach them, because the scroll area itself ran off the edge.
/// </summary>
public class DialogSizeTests
{
    private const double Preferred = 1000, Minimum = 520;

    /// <summary>With room to spare, the dialog gets what it asked for.</summary>
    [Theory]
    [InlineData(1920)]
    [InlineData(1600)]
    [InlineData(1000 + DialogSize.Chrome)]
    public void APlentifulWindowGivesThePreferredSize(double available)
        => Assert.Equal(Preferred, DialogSize.Fit(available, Preferred, Minimum), 6);

    /// <summary>The case that was broken: a window too small for the preferred size.</summary>
    [Fact]
    public void ACrampedWindowShrinksTheDialog()
    {
        var fitted = DialogSize.Fit(900, Preferred, Minimum);

        Assert.True(fitted < Preferred, "the dialog must give way to the window");
        Assert.Equal(900 - DialogSize.Chrome, fitted, 6);
    }

    /// <summary>Whatever the window, the dialog leaves room for the chrome around it.</summary>
    [Theory]
    [InlineData(700)]
    [InlineData(900)]
    [InlineData(1100)]
    public void TheResultLeavesRoomForTheChrome(double available)
    {
        var fitted = DialogSize.Fit(available, Preferred, Minimum);

        Assert.True(fitted <= Math.Max(Minimum, available - DialogSize.Chrome) + 1e-9,
            $"{fitted} does not fit in {available}");
    }

    /// <summary>
    /// Below the minimum the dialog stops shrinking. By then the window is smaller than anything
    /// could be laid out in, and a dialog squeezed to nothing is worse than one that overflows.
    /// </summary>
    [Theory]
    [InlineData(400)]
    [InlineData(200)]
    [InlineData(1)]
    public void ATinyWindowStillLeavesAUsableDialog(double available)
        => Assert.Equal(Minimum, DialogSize.Fit(available, Preferred, Minimum), 6);

    /// <summary>
    /// A dialog opening before its root has been measured should look right rather than defensively
    /// small — the size it prefers is the better guess than the minimum.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AnUnmeasurableWindowMeansThePreferredSize(double available)
        => Assert.Equal(Preferred, DialogSize.Fit(available, Preferred, Minimum), 6);

    /// <summary>The answer is always something a dialog can be laid out at.</summary>
    [Theory]
    [InlineData(1920)]
    [InlineData(900)]
    [InlineData(300)]
    public void TheResultIsAlwaysUsable(double available)
    {
        var fitted = DialogSize.Fit(available, Preferred, Minimum);

        Assert.True(double.IsFinite(fitted) && fitted >= Minimum, $"got {fitted}");
        Assert.True(fitted <= Preferred);
    }
}
