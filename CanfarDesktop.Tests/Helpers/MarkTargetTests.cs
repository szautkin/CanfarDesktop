using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// Which image a set of marks belongs to.
///
/// <para>A multi-extension FITS file is several images. Keyed by path alone, a pixel mark drawn on
/// one chip of a forty-one-chip mosaic reappeared at the same pixel on every other chip. These are the
/// rules for the key that fixes that, and for reading an MCP call's idea of "this file" into it.</para>
/// </summary>
public class MarkTargetTests
{
    private const string File = @"C:\data\1832496o.fits.fz";

    // ── The key ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AKeyNamesTheFileAndTheExtension()
        => Assert.Equal(@"C:\data\1832496o.fits.fz#5", MarkTarget.Key(File, 5));

    [Fact]
    public void AKeyReadsBackToWhatMadeIt()
        => Assert.Equal((File, (int?)5), MarkTarget.Parse(MarkTarget.Key(File, 5)));

    /// <summary>A bare path — a cube, or a mark from before extensions were tracked — has no extension.</summary>
    [Fact]
    public void ABarePathHasNoExtension()
        => Assert.Equal((File, (int?)null), MarkTarget.Parse(File));

    /// <summary>
    /// <c>#</c> is legal in a Windows file name. A file called <c>odd#name.fits</c> is a path, not
    /// extension "name.fits" of something called <c>odd</c> — which is why only an all-digit tail after
    /// the LAST <c>#</c> counts.
    /// </summary>
    [Theory]
    [InlineData(@"C:\odd#name.fits")]
    [InlineData(@"C:\run#3\frame.fits")]
    [InlineData(@"C:\trailing#")]
    [InlineData(@"#12")]
    public void AHashInAFileNameIsNotAnExtension(string path)
        => Assert.Null(MarkTarget.Parse(path).Hdu);

    /// <summary>...while a file with a hash in its name can still carry an extension of its own.</summary>
    [Fact]
    public void AFileWithAHashInItsNameStillTakesAnExtension()
        => Assert.Equal((@"C:\run#3\frame.fits", (int?)2), MarkTarget.Parse(@"C:\run#3\frame.fits#2"));

    [Fact]
    public void TwoExtensionsOfOneFileAreTheSameFile()
        => Assert.True(MarkTarget.SameFile(MarkTarget.Key(File, 1), MarkTarget.Key(File, 39)));

    /// <summary>A path typed by an agent and one read back from a dialog need not agree on case.</summary>
    [Fact]
    public void SameFileIgnoresCase()
        => Assert.True(MarkTarget.SameFile(File.ToUpperInvariant(), MarkTarget.Key(File, 1)));

    [Fact]
    public void DifferentFilesAreNotTheSameFile()
        => Assert.False(MarkTarget.SameFile(@"C:\a.fits#1", @"C:\b.fits#1"));

    // ── What an MCP call means ──────────────────────────────────────────────────────────────────

    private static readonly string OnScreen = MarkTarget.Key(File, 5);

    [Fact]
    public void NothingNamedMeansWhatIsOnScreen()
        => Assert.Equal(OnScreen, MarkTarget.Resolve(null, null, OnScreen, perExtension: true));

    /// <summary>
    /// "This file" means the extension the person is looking at. Taking it as the first extension
    /// instead would put an agent's mark on a chip nobody is looking at.
    /// </summary>
    [Fact]
    public void TheFileOnScreenMeansTheExtensionOnScreen()
        => Assert.Equal(OnScreen, MarkTarget.Resolve(File, null, OnScreen, perExtension: true));

    [Fact]
    public void AnExplicitExtensionWins()
        => Assert.Equal(MarkTarget.Key(File, 9), MarkTarget.Resolve(File, 9, OnScreen, perExtension: true));

    [Fact]
    public void AnExplicitExtensionAppliesToTheFileOnScreenWhenNoFileIsNamed()
        => Assert.Equal(MarkTarget.Key(File, 9), MarkTarget.Resolve(null, 9, OnScreen, perExtension: true));

    [Fact]
    public void AFullKeyIsTakenAsGiven()
        => Assert.Equal(MarkTarget.Key(File, 2), MarkTarget.Resolve(MarkTarget.Key(File, 2), null, OnScreen, perExtension: true));

    /// <summary>
    /// A file not on screen stays a bare path: it lands on that file's first image extension when it
    /// is opened, which is the only sensible default when nobody said which.
    /// </summary>
    [Fact]
    public void AnotherFileWithNoExtensionStaysBare()
        => Assert.Equal(@"C:\data\other.fits", MarkTarget.Resolve(@"C:\data\other.fits", null, OnScreen, perExtension: true));

    /// <summary>A cube has one image, so its key is its path whatever it is given.</summary>
    [Fact]
    public void ACubeKeyIsItsPath()
    {
        Assert.Equal(@"C:\c.fits", MarkTarget.Resolve(@"C:\c.fits#3", null, null, perExtension: false));
        Assert.Equal(@"C:\c.fits", MarkTarget.Resolve(null, null, @"C:\c.fits", perExtension: false));
    }

    [Fact]
    public void NothingNamedAndNothingOnScreenIsNothing()
        => Assert.Null(MarkTarget.Resolve(null, null, null, perExtension: true));
}
