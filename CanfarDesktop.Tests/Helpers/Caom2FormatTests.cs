using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

public class Caom2FormatTests
{
    [Theory]
    [InlineData(null, "—")]
    [InlineData(0L, "0 B")]
    [InlineData(512L, "512 B")]
    [InlineData(123456789L, "117.7 MB")]
    public void Bytes_Formats(long? input, string expected)
        => Assert.Equal(expected, Caom2Format.Bytes(input));

    [Fact]
    public void Wavelength_PicksFriendlyUnit()
    {
        Assert.Equal("1.755 µm", Caom2Format.Wavelength(1.755e-6));
        Assert.Equal("500 nm", Caom2Format.Wavelength(500e-9));
        Assert.Equal("—", Caom2Format.Wavelength(null));
        Assert.Equal("—", Caom2Format.Wavelength(0));
    }

    [Fact]
    public void MjdToDate_KnownEpoch()
    {
        // MJD 59000 = 2020-05-31; +0.5 day = 12:00 UTC.
        Assert.StartsWith("2020-05-31 12:00", Caom2Format.MjdToDate(59000.5));
        Assert.Equal("—", Caom2Format.MjdToDate(null));
    }

    [Theory]
    [InlineData("cadc:JWST/jw01147_nircam_f200w_i2d.fits", "jw01147_nircam_f200w_i2d.fits")]
    [InlineData("plainname.fits", "plainname.fits")]
    public void ArtifactFileName_TakesLastSegment(string uri, string expected)
        => Assert.Equal(expected, Caom2Format.ArtifactFileName(uri));

    [Fact]
    public void Bool_AndText_HandleNulls()
    {
        Assert.Equal("—", Caom2Format.Bool(null));
        Assert.Equal("Yes", Caom2Format.Bool(true));
        Assert.Equal("—", Caom2Format.Text("  "));
        Assert.Equal("hi", Caom2Format.Text("hi"));
    }
}
