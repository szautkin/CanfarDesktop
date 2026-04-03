using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

public class CellFormatterTests
{
    // MJD date formatting
    [Fact]
    public void Format_StartDate_ConvertsMjdToIsoDate()
    {
        // MJD 51544.5 = 2000-01-01T12:00:00
        var result = CellFormatter.Format("startdate", "51544.5");
        Assert.Equal("2000-01-01", result);
    }

    [Fact]
    public void Format_StartDate_InvalidValue_ReturnsRaw()
    {
        Assert.Equal("not-a-number", CellFormatter.Format("startdate", "not-a-number"));
    }

    [Fact]
    public void Format_StartDate_Empty_ReturnsEmpty()
    {
        Assert.Equal("", CellFormatter.Format("startdate", ""));
        Assert.Equal("", CellFormatter.Format("startdate", "   "));
    }

    // Coordinate formatting
    [Fact]
    public void Format_RA_FormatsTo5DecimalPlaces()
    {
        Assert.Equal("10.68400", CellFormatter.Format("ra(j20000)", "10.684"));
    }

    [Fact]
    public void Format_Dec_FormatsTo5DecimalPlaces()
    {
        Assert.Equal("-41.26900", CellFormatter.Format("dec(j20000)", "-41.269"));
    }

    // Integration time formatting
    [Theory]
    [InlineData("3600", "1h")]
    [InlineData("7200", "2h")]
    [InlineData("5400", "1.5h")]
    [InlineData("300", "5m")]
    [InlineData("90", "1.5m")]
    [InlineData("45", "45s")]
    [InlineData("0.5", "0.5s")]
    public void Format_IntTime_ConvertsToHumanReadable(string input, string expected)
    {
        Assert.Equal(expected, CellFormatter.Format("inttime", input));
    }

    // Calibration level
    [Theory]
    [InlineData("0", "Raw")]
    [InlineData("1", "Cal")]
    [InlineData("2", "Product")]
    [InlineData("3", "Composite")]
    [InlineData("4", "4")]
    public void Format_CalLevel_MapsToLabel(string input, string expected)
    {
        Assert.Equal(expected, CellFormatter.Format("callev", input));
    }

    // Boolean
    [Theory]
    [InlineData("true", "\u2713")]
    [InlineData("1", "\u2713")]
    [InlineData("false", "")]
    [InlineData("0", "")]
    public void Format_Boolean_ShowsCheckmark(string input, string expected)
    {
        Assert.Equal(expected, CellFormatter.Format("download", input));
    }

    // Wavelength
    [Fact]
    public void Format_Wavelength_SmallValue_UsesScientific()
    {
        var result = CellFormatter.Format("minwavelength", "0.0000005");
        Assert.Contains("E", result);
    }

    [Fact]
    public void Format_Wavelength_NormalValue_UsesStandard()
    {
        var result = CellFormatter.Format("minwavelength", "0.5");
        Assert.Equal("0.5", result);
    }

    // Timestamp
    [Fact]
    public void Format_DataRelease_CleansIsoTimestamp()
    {
        Assert.Equal("2023-06-15 12:30:45", CellFormatter.Format("datarelease", "2023-06-15T12:30:45.123Z"));
    }

    // CleanKey
    [Theory]
    [InlineData("\"RA (J2000.0)\"", "ra(j20000)")]
    [InlineData("Collection", "collection")]
    [InlineData("\"Target Name\"", "targetname")]
    [InlineData("Plane.publisherID", "planepublisherid")]
    public void CleanKey_NormalizesHeaders(string input, string expected)
    {
        Assert.Equal(expected, CellFormatter.CleanKey(input));
    }

    // DefaultVisibleKeys
    [Fact]
    public void DefaultVisibleKeys_ContainsExpectedColumns()
    {
        Assert.Contains("collection", CellFormatter.DefaultVisibleKeys);
        Assert.Contains("targetname", CellFormatter.DefaultVisibleKeys);
        Assert.Contains("ra(j20000)", CellFormatter.DefaultVisibleKeys);
        Assert.Contains("instrument", CellFormatter.DefaultVisibleKeys);
        Assert.Contains("download", CellFormatter.DefaultVisibleKeys);
        Assert.Contains("preview", CellFormatter.DefaultVisibleKeys);
    }

    // Unknown column — passthrough
    [Fact]
    public void Format_UnknownColumn_ReturnsRawTrimmed()
    {
        Assert.Equal("hello world", CellFormatter.Format("unknowncolumn", "  hello world  "));
    }
}
