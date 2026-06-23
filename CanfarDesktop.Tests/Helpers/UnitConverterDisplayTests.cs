using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

public class UnitConverterDisplayTests
{
    // ── Spectral (metres in, cross-dimension via macOS CGS constants) ─────────

    [Fact]
    public void ConvertSpectral_Wavelength_ScalesByUnitFactor()
        => Assert.Equal(500.0, UnitConverter.ConvertSpectral(5e-7, "nm")!.Value, 6);

    [Fact]
    public void ConvertSpectral_RejectsNonPositive()
    {
        Assert.Null(UnitConverter.ConvertSpectral(0, "nm"));
        Assert.Null(UnitConverter.ConvertSpectral(-1, "nm"));
        Assert.Null(UnitConverter.ConvertSpectral(double.NaN, "nm"));
        Assert.Null(UnitConverter.ConvertSpectral(1e-7, "bogus"));
    }

    [Theory]
    [InlineData("0.0000005", "nm", "500.0 nm")]  // 5e-7 m = 500 nm  (>=100 → 1dp)
    [InlineData("1", "m", "1.00 m")]              // (>=1 → 2dp)
    [InlineData("1", "hz", "299792500.0 Hz")]     // c / 1m
    [InlineData("1", "ghz", "0.300 GHz")]         // 0.2997925 GHz (>=0.001 → 3dp)
    public void FormatSpectral_RendersValueAndLabel(string raw, string unit, string expected)
        => Assert.Equal(expected, UnitConverter.FormatSpectral(raw, unit));

    [Fact]
    public void FormatSpectral_PassthroughOnUnparseable()
        => Assert.Equal("abc", UnitConverter.FormatSpectral("abc", "nm"));

    [Fact]
    public void SpectralUnitChoices_Has14UnitsInOrder()
    {
        var ids = UnitConverter.SpectralUnitChoices.Select(c => c.Id).ToArray();
        Assert.Equal(new[] { "m", "cm", "mm", "um", "nm", "a", "hz", "khz", "mhz", "ghz", "ev", "kev", "mev", "gev" }, ids);
    }

    // ── Duration / angle / area ───────────────────────────────────────────────

    [Theory]
    [InlineData("3600", "hours", "1.000 h")]
    [InlineData("90", "minutes", "1.500 m")]
    [InlineData("45", "seconds", "45.000 s")]
    [InlineData("86400", "days", "1.000 d")]
    public void FormatDuration_ScalesAndLabels(string raw, string unit, string expected)
        => Assert.Equal(expected, UnitConverter.FormatDuration(raw, unit));

    [Fact]
    public void FormatAngle_MasAndArcsec()
    {
        Assert.Equal("3600000.000 mas", UnitConverter.FormatAngle("1", "milliarcseconds"));
        Assert.StartsWith("3600.000 ", UnitConverter.FormatAngle("1", "arcseconds")); // label is the double-prime glyph
    }

    [Fact]
    public void FormatArea_SquareUnits()
        => Assert.Equal("3600.000 sq arcmin", UnitConverter.FormatArea("1", "sq_arcmin"));

    [Theory]
    [InlineData(0.0, "0.000")]
    [InlineData(1.5, "1.500")]
    [InlineData(0.0001, "0.000100")] // below 0.001 → 6dp
    public void AdaptivePrecision_SwitchesAtMilli(double v, string expected)
        => Assert.Equal(expected, UnitConverter.AdaptivePrecision(v));
}
