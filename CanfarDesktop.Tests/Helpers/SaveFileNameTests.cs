using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// The FileSavePicker appends the selected file type's extension to the name it is given. Handing it a
/// COMPLETE filename and then offering only ".fits" doubled the extension on everything that was not
/// literally a .fits — an fpack artifact was saved as <c>x.fits.fz.fits</c>.
/// </summary>
public class SaveFileNameTests
{
    /// <summary>
    /// The property that matters: whatever the picker is handed must reassemble into the name the
    /// archive gave us. Anything else is a doubled — or truncated — extension.
    /// </summary>
    [Theory]
    [InlineData("1100689o.fits")]
    [InlineData("jw01234-o001_t001_nircam_i2d.fits")]
    [InlineData("1100689p.fits.fz")]          // fpack: the case that was broken
    [InlineData("jw01234_asn.json")]
    [InlineData("weird.name.with.dots.fits")]
    public void TheStemAndExtensionReassembleIntoTheOriginalName(string fileName)
        => Assert.Equal(fileName, SaveFileName.Combine(SaveFileName.ForPicker(fileName)));

    /// <summary>fpack is the regression: only the LAST extension is split off.</summary>
    [Fact]
    public void AnFpackNameKeepsFitsInTheStem()
    {
        var (stem, ext) = SaveFileName.ForPicker("1100689p.fits.fz");

        Assert.Equal("1100689p.fits", stem);
        Assert.Equal(".fz", ext);
    }

    [Fact]
    public void APlainFitsNameSplitsAtTheDot()
    {
        var (stem, ext) = SaveFileName.ForPicker("1100689o.fits");

        Assert.Equal("1100689o", stem);
        Assert.Equal(".fits", ext);
    }

    /// <summary>
    /// A publisher id resolves to a bare identifier with no extension. These are FITS downloads, so
    /// that is what it is assumed to be — and the result still round-trips.
    /// </summary>
    [Fact]
    public void ANameWithNoExtensionGetsFits()
    {
        var parts = SaveFileName.ForPicker("1100689o");

        Assert.Equal(("1100689o", ".fits"), parts);
        Assert.Equal("1100689o.fits", SaveFileName.Combine(parts));
    }

    /// <summary>A trailing dot is not an extension to offer as a file type.</summary>
    [Fact]
    public void ATrailingDotIsNotAnExtension()
    {
        var (stem, ext) = SaveFileName.ForPicker("1100689o.");

        Assert.Equal("1100689o", stem);
        Assert.Equal(".fits", ext);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingUsableStillYieldsAnExtension(string? fileName)
    {
        var (stem, ext) = SaveFileName.ForPicker(fileName);

        Assert.Equal("", stem);
        Assert.Equal(".fits", ext);
    }

    /// <summary>
    /// The shape the bug had: the extension must appear exactly once in what the picker will produce.
    /// </summary>
    [Theory]
    [InlineData("1100689p.fits.fz", ".fits")]
    [InlineData("1100689o.fits", ".fits")]
    [InlineData("jw01234_asn.json", ".json")]
    public void TheExtensionIsNeverDoubled(string fileName, string ext)
    {
        var combined = SaveFileName.Combine(SaveFileName.ForPicker(fileName));

        Assert.False(combined.EndsWith(ext + ext, StringComparison.OrdinalIgnoreCase),
            $"{fileName} became {combined}");
    }
}
