using Xunit;
using CanfarDesktop.Models.Fits;

namespace CanfarDesktop.Tests.Models.Fits;

public class WorldCoordinateTests
{
    [Fact]
    public void FormattedRa_CorrectFormat()
    {
        var coord = new WorldCoordinate(180.0, 45.0);
        Assert.Equal("12h00m00.00s", coord.FormattedRa);
    }

    [Fact]
    public void FormattedDec_CorrectFormat()
    {
        var coord = new WorldCoordinate(180.0, 45.0);
        Assert.StartsWith("+45", coord.FormattedDec);
    }

    [Fact]
    public void FormattedDec_Negative()
    {
        var coord = new WorldCoordinate(0, -33.5);
        Assert.StartsWith("-33", coord.FormattedDec);
    }

    [Fact]
    public void Display_CombinesRaAndDec()
    {
        var coord = new WorldCoordinate(180.0, 45.0);
        Assert.Contains("RA", coord.Display);
        Assert.Contains("Dec", coord.Display);
        Assert.Contains("12h00m", coord.Display);
    }

    [Fact]
    public void RecordEquality_SameValues_Equal()
    {
        var a = new WorldCoordinate(10.5, 20.3);
        var b = new WorldCoordinate(10.5, 20.3);
        Assert.Equal(a, b);
    }

    [Fact]
    public void RecordEquality_DifferentValues_NotEqual()
    {
        var a = new WorldCoordinate(10.5, 20.3);
        var b = new WorldCoordinate(10.5, 20.4);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void RecordEquality_Null()
    {
        var a = new WorldCoordinate(10.0, 20.0);
        Assert.False(a.Equals(null));
    }
}
