using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

public class CellFormatterUnitTests
{
    [Fact]
    public void Ra_DefaultsToSexagesimal_DegreesOnRequest()
    {
        Assert.Equal("12:00:00.00", CellFormatter.Format("RA (J2000.0)", "180", null)); // default hms
        Assert.Equal("180.000000", CellFormatter.Format("RA (J2000.0)", "180", "degrees"));
    }

    [Fact]
    public void Dec_DefaultsToSexagesimal_AlwaysSignedDegrees()
    {
        Assert.Equal("-30:00:00.0", CellFormatter.Format("Dec (J2000.0)", "-30", "dms"));
        Assert.Equal("-30.000000", CellFormatter.Format("Dec (J2000.0)", "-30", "degrees"));
        Assert.Equal("+45.000000", CellFormatter.Format("Dec (J2000.0)", "45", "degrees")); // forced +
    }

    [Theory]
    [InlineData("Min Wavelength", "0.0000005", "nm", "500.0 nm")]
    [InlineData("Int Time", "3600", "hours", "1.000 h")]
    [InlineData("Int Time", "3600", null, "1h")]        // default = readable legacy adaptive
    [InlineData("Start Date", "59000", "mjd", "59000")] // raw MJD
    public void UnitColumns_RenderInChosenUnit(string header, string raw, string? unit, string expected)
        => Assert.Equal(expected, CellFormatter.Format(header, raw, unit));

    [Fact]
    public void PixelScale_Arcseconds()
        => Assert.StartsWith("3600.000 ", CellFormatter.Format("Pixel Scale", "1", "arcseconds"));

    [Fact]
    public void Empty_StaysEmpty()
        => Assert.Equal("", CellFormatter.Format("Min Wavelength", "  ", "nm"));

    [Fact]
    public void NonMenuColumn_UsesLegacyFormatter()
        => Assert.Equal("✓", CellFormatter.Format("Download", "true", "ignored")); // bool checkmark, unit ignored
}
