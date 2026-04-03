using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

public class UnitConverterTests
{
    // Wavelength conversions
    [Theory]
    [InlineData("500", "nm", 5e-7)]
    [InlineData("1", "m", 1.0)]
    [InlineData("21", "cm", 0.21)]
    [InlineData("1.0", "mm", 1e-3)]
    [InlineData("10", "\u00b5m", 1e-5)]  // µm
    [InlineData("5000", "\u00c5", 5e-7)]  // Å
    public void TryConvertToMetres_Wavelength(string value, string unit, double expected)
    {
        Assert.True(UnitConverter.TryConvertToMetres(value, unit, out var metres));
        Assert.Equal(expected, metres, 5);
    }

    // Frequency conversions (λ = c / f)
    [Fact]
    public void TryConvertToMetres_Frequency_1420MHz()
    {
        // 21cm hydrogen line at 1420.405 MHz
        Assert.True(UnitConverter.TryConvertToMetres("1420.405", "MHz", out var metres));
        Assert.Equal(0.211, metres, 3); // ~21.1 cm
    }

    [Fact]
    public void TryConvertToMetres_Frequency_1GHz()
    {
        Assert.True(UnitConverter.TryConvertToMetres("1", "GHz", out var metres));
        Assert.Equal(0.2998, metres, 3); // ~30 cm
    }

    // Energy conversions (λ = hc / E)
    [Fact]
    public void TryConvertToMetres_Energy_13_6eV()
    {
        // Lyman limit at 13.6 eV ≈ 91.2 nm
        Assert.True(UnitConverter.TryConvertToMetres("13.6", "eV", out var metres));
        Assert.Equal(9.12e-8, metres, 2);
    }

    [Fact]
    public void TryConvertToMetres_Energy_1keV()
    {
        var result = UnitConverter.TryConvertToMetres("1", "keV", out var metres);
        Assert.True(result, $"TryConvertToMetres returned false for 1 keV");
        Assert.True(metres > 1e-10 && metres < 2e-9, $"Expected X-ray range, got {metres}");
    }

    [Fact]
    public void TryConvertToMetres_InvalidValue_ReturnsFalse()
    {
        Assert.False(UnitConverter.TryConvertToMetres("abc", "nm", out _));
    }

    [Fact]
    public void TryConvertToMetres_ZeroValue_ReturnsFalse()
    {
        Assert.False(UnitConverter.TryConvertToMetres("0", "nm", out _));
    }

    [Fact]
    public void TryConvertToMetres_UnknownUnit_ReturnsFalse()
    {
        Assert.False(UnitConverter.TryConvertToMetres("500", "furlongs", out _));
    }

    // Time to seconds
    [Theory]
    [InlineData("1", "s", 1.0)]
    [InlineData("5", "m", 300.0)]
    [InlineData("2", "h", 7200.0)]
    [InlineData("1", "d", 86400.0)]
    public void TryConvertToSeconds(string value, string unit, double expected)
    {
        Assert.True(UnitConverter.TryConvertToSeconds(value, unit, out var secs));
        Assert.Equal(expected, secs, 1);
    }

    // Time to days
    [Theory]
    [InlineData("1", "d", 1.0)]
    [InlineData("24", "h", 1.0)]
    [InlineData("86400", "s", 1.0)]
    [InlineData("1", "y", 365.25)]
    public void TryConvertToDays(string value, string unit, double expected)
    {
        Assert.True(UnitConverter.TryConvertToDays(value, unit, out var days));
        Assert.Equal(expected, days, 5);
    }

    // Pixel scale to degrees
    [Theory]
    [InlineData("3600", "arcsec", 1.0)]
    [InlineData("60", "arcmin", 1.0)]
    [InlineData("1", "deg", 1.0)]
    [InlineData("10", "arcsec", 0.002778)]
    public void TryConvertToDegrees(string value, string unit, double expected)
    {
        Assert.True(UnitConverter.TryConvertToDegrees(value, unit, out var deg));
        Assert.Equal(expected, deg, 3);
    }

    // Suffix extraction
    [Theory]
    [InlineData("500nm", "500", "nm")]
    [InlineData("1.4GHz", "1.4", "ghz")]
    [InlineData("100", "100", null)]
    [InlineData("13.6eV", "13.6", "ev")]
    public void ExtractSpectralSuffix(string input, string expectedNumeric, string? expectedUnit)
    {
        var (numeric, unit) = UnitConverter.ExtractSpectralSuffix(input);
        Assert.Equal(expectedNumeric, numeric);
        Assert.Equal(expectedUnit, unit);
    }

    // Inverse unit detection
    [Theory]
    [InlineData("GHz", true)]
    [InlineData("eV", true)]
    [InlineData("nm", false)]
    [InlineData("m", false)]
    public void IsInverseUnit(string unit, bool expected)
    {
        Assert.Equal(expected, UnitConverter.IsInverseUnit(unit));
    }
}
