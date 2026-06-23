using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

public class ColumnUnitCatalogTests
{
    [Theory]
    [InlineData("RA (J2000.0)", true)]   // cleans to ra(j20000)
    [InlineData("Min Wavelength", true)]
    [InlineData("Int Time", true)]
    [InlineData("Collection", false)]
    [InlineData("Instrument", false)]
    public void HasMenu_OnlyForUnitColumns(string header, bool expected)
        => Assert.Equal(expected, ColumnUnitCatalog.HasMenu(header));

    [Fact]
    public void RaDec_OfferSexagesimalThenDegrees_DefaultSexagesimal()
    {
        Assert.Equal(new[] { "hms", "degrees" }, ColumnUnitCatalog.AvailableUnits("RA (J2000.0)").Select(c => c.Id));
        Assert.Equal("hms", ColumnUnitCatalog.DefaultUnitId("RA (J2000.0)"));
        Assert.Equal("dms", ColumnUnitCatalog.DefaultUnitId("Dec (J2000.0)"));
    }

    [Theory]
    [InlineData("Min Wavelength", "m")]
    [InlineData("Int Time", "seconds")]
    [InlineData("Pixel Scale", "arcseconds")]
    [InlineData("Field Of View", "sq_deg")]
    [InlineData("Start Date", "calendar")]
    public void Defaults_MatchMacOs(string header, string expectedDefault)
        => Assert.Equal(expectedDefault, ColumnUnitCatalog.DefaultUnitId(header));

    [Fact]
    public void Spectral_Has14Units()
        => Assert.Equal(14, ColumnUnitCatalog.AvailableUnits("Min Wavelength").Count);

    [Fact]
    public void PositionResolution_HasNoDegreesOption()
    {
        var ids = ColumnUnitCatalog.AvailableUnits("Position Resolution").Select(c => c.Id).ToList();
        Assert.Equal(new[] { "milliarcseconds", "arcseconds", "arcminutes" }, ids);
        Assert.DoesNotContain("degrees", ids);
    }
}
