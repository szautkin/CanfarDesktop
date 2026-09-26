using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>Result rows as a spreadsheet takes them when pasted.</summary>
public class TabularTextTests
{
    [Fact]
    public void Rows_AreTabSeparated_UnderTheirColumnNames()
    {
        var text = TabularText.Of(["Target", "RA"], [["M31", "00:42:44.3"], ["M33", "01:33:50.9"]]);

        Assert.Equal(string.Join(Environment.NewLine, "Target\tRA", "M31\t00:42:44.3", "M33\t01:33:50.9"), text);
    }

    /// <summary>A value's own tab or line break would split its row in two; it becomes a space.</summary>
    [Fact]
    public void AValuesOwnTabsAndLineBreaks_DoNotSplitItsRow()
    {
        var lines = TabularText.Of(["Title"], [["M31\thalo\r\nsurvey\nyear 2"]]).Split(Environment.NewLine);

        Assert.Equal(2, lines.Length);
        Assert.Equal("M31 halo survey year 2", lines[1]);
    }
}
