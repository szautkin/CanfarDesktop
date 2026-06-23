using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

public class SexagesimalFormatTests
{
    [Theory]
    [InlineData(0.0, "00:00:00.00")]
    [InlineData(15.0, "01:00:00.00")]   // 15° = 1h
    [InlineData(180.0, "12:00:00.00")]
    [InlineData(270.0, "18:00:00.00")]
    [InlineData(-15.0, "23:00:00.00")]  // negative wraps into [0,24)h
    public void FormatRaHms_Hours(double deg, string expected)
        => Assert.Equal(expected, Sexagesimal.FormatRaHms(deg));

    [Fact]
    public void FormatRaHms_WrapsAtFullCircle_NeverShows24h()
        => Assert.Equal("00:00:00.00", Sexagesimal.FormatRaHms(360.0));

    [Fact]
    public void FormatRaHms_PassthroughOnUnparseable()
        => Assert.Equal("n/a", Sexagesimal.FormatRaHms("n/a"));

    [Theory]
    [InlineData(0.0, "+00:00:00.0")]    // always-signed, + for >= 0
    [InlineData(-30.0, "-30:00:00.0")]
    [InlineData(45.5, "+45:30:00.0")]   // 0.5° = 30'
    [InlineData(90.0, "+90:00:00.0")]
    [InlineData(-90.0, "-90:00:00.0")]
    public void FormatDecDms_Degrees(double deg, string expected)
        => Assert.Equal(expected, Sexagesimal.FormatDecDms(deg));

    [Theory]
    [InlineData("100")]   // out of [-90, 90]
    [InlineData("-91")]
    [InlineData("abc")]
    public void FormatDecDms_PassthroughOnInvalidOrOutOfRange(string raw)
        => Assert.Equal(raw, Sexagesimal.FormatDecDms(raw));
}
