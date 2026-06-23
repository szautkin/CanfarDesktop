using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

public class SexagesimalTests
{
    [Theory]
    [InlineData("12:00:00", 180.0)]      // 12h = 180°
    [InlineData("00:00:00", 0.0)]
    [InlineData("06 00 00", 90.0)]       // space-separated
    [InlineData("01:19:12", 19.8)]       // CADC example
    public void ParseRa_Valid(string input, double expectedDeg)
        => Assert.Equal(expectedDeg, Sexagesimal.ParseRa(input)!.Value, 6);

    [Theory]
    [InlineData("24:00:00")]   // hours out of range
    [InlineData("10:60:00")]   // minutes out of range
    [InlineData("10")]         // too few fields
    [InlineData("")]
    [InlineData(null)]
    public void ParseRa_Invalid_ReturnsNull(string? input)
        => Assert.Null(Sexagesimal.ParseRa(input));

    [Theory]
    [InlineData("+45:30:00", 45.5)]
    [InlineData("-33:30:00", -33.5)]
    [InlineData("00:00:00", 0.0)]
    [InlineData("+42:06:04", 42.101111)]
    public void ParseDec_Valid(string input, double expectedDeg)
        => Assert.Equal(expectedDeg, Sexagesimal.ParseDec(input)!.Value, 5);

    [Theory]
    [InlineData("+91:00:00")]  // degrees out of range
    [InlineData("-30:60:00")]  // minutes out of range
    [InlineData("30")]         // too few fields
    [InlineData(null)]
    public void ParseDec_Invalid_ReturnsNull(string? input)
        => Assert.Null(Sexagesimal.ParseDec(input));
}
