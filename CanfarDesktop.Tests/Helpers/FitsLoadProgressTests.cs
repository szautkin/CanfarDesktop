using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// What to show while a FITS file is being read.
///
/// A mosaic frame has forty-one extensions, each decompressed in turn, and takes tens of seconds. The
/// viewer showed a twenty-pixel spinner for all of it — which says something is happening, and not
/// whether it is nearly done or stuck.
/// </summary>
public class FitsLoadProgressTests
{
    private static FitsParseProgress At(int hdus, long read, long total, string? name = null)
        => new(hdus, read, total, name);

    [Theory]
    [InlineData(0, 100, 0.0)]
    [InlineData(50, 100, 0.5)]
    [InlineData(100, 100, 1.0)]
    public void TheFractionIsHowFarThroughTheFile(long read, long total, double expected)
        => Assert.Equal(expected, FitsLoadProgress.Fraction(At(1, read, total))!.Value, 6);

    /// <summary>
    /// Unknown length is not zero. A bar that has not moved and a bar that cannot move look the same,
    /// and only one of them means something is wrong — so this leaves it indeterminate instead.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AnUnmeasurableStreamHasNoFraction(long total)
        => Assert.Null(FitsLoadProgress.Fraction(At(1, 50, total)));

    /// <summary>A position past the end is still a position: clamped, not rejected.</summary>
    [Fact]
    public void ReadingPastTheEndStaysAtOne()
        => Assert.Equal(1.0, FitsLoadProgress.Fraction(At(1, 500, 100))!.Value, 6);

    [Fact]
    public void ANegativePositionIsNotAFraction()
        => Assert.Null(FitsLoadProgress.Fraction(At(1, -5, 100)));

    // ── What it says ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The extension's name is the useful part on a mosaic: it is the difference between "still
    /// going" and "on ccd34 of what looks like forty".
    /// </summary>
    [Fact]
    public void ItNamesTheExtensionBeingRead()
    {
        var text = FitsLoadProgress.Describe("1832496o.fits.fz", At(7, 100, 1000, "ccd06"));

        Assert.Contains("ccd06", text);
        Assert.Contains("7", text);
    }

    /// <summary>A file whose extensions have no names still says how many are done.</summary>
    [Fact]
    public void WithoutNamesItCountsInstead()
    {
        var text = FitsLoadProgress.Describe("stack.fits", At(3, 100, 1000));

        Assert.Contains("stack.fits", text);
        Assert.Contains("3", text);
    }

    /// <summary>And at the very start there is still something truthful to show.</summary>
    [Fact]
    public void AtTheStartItNamesTheFile()
    {
        var text = FitsLoadProgress.Describe("m51.fits", At(0, 0, 1000));

        Assert.Contains("m51.fits", text);
    }

    /// <summary>Whatever it is given, it says something rather than nothing.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyExtensionNameFallsBackRatherThanShowingABlank(string? name)
    {
        var text = FitsLoadProgress.Describe("m51.fits", At(2, 10, 100, name));

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.Contains("m51.fits", text);
    }
}
