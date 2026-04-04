using Xunit;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;

namespace CanfarDesktop.Tests.Helpers;

public class ResultFilterTests
{
    private static SearchResultRow MakeRow(params (string key, string value)[] values)
    {
        var row = new SearchResultRow();
        foreach (var (k, v) in values) row.Values[k] = v;
        return row;
    }

    private static readonly Func<string, string> Identity = k => k;

    [Fact]
    public void Filter_SingleColumn_MatchesSubstring()
    {
        var rows = new List<SearchResultRow>
        {
            MakeRow(("name", "NGC 1234")),
            MakeRow(("name", "M31")),
            MakeRow(("name", "NGC 5678")),
        };
        var filters = new Dictionary<string, string> { ["name"] = "NGC" };
        var result = ResultFilter.Filter(rows, filters, Identity);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Filter_CaseInsensitive()
    {
        var rows = new List<SearchResultRow>
        {
            MakeRow(("name", "Andromeda")),
        };
        var filters = new Dictionary<string, string> { ["name"] = "andromeda" };
        Assert.Single(ResultFilter.Filter(rows, filters, Identity));

        filters["name"] = "ANDROMEDA";
        Assert.Single(ResultFilter.Filter(rows, filters, Identity));
    }

    [Fact]
    public void Filter_MultipleColumns_AndLogic()
    {
        var rows = new List<SearchResultRow>
        {
            MakeRow(("name", "M31"), ("instrument", "WFC3")),
            MakeRow(("name", "M31"), ("instrument", "NIRCAM")),
            MakeRow(("name", "M33"), ("instrument", "WFC3")),
        };
        var filters = new Dictionary<string, string>
        {
            ["name"] = "M31",
            ["instrument"] = "WFC3"
        };
        var result = ResultFilter.Filter(rows, filters, Identity);
        Assert.Single(result);
        Assert.Equal("M31", result[0].Get("name"));
        Assert.Equal("WFC3", result[0].Get("instrument"));
    }

    [Fact]
    public void Filter_EmptyFilters_ReturnsAll()
    {
        var rows = new List<SearchResultRow>
        {
            MakeRow(("a", "1")),
            MakeRow(("a", "2")),
        };
        var result = ResultFilter.Filter(rows, new Dictionary<string, string>(), Identity);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Filter_WhitespaceFilterText_Ignored()
    {
        var rows = new List<SearchResultRow> { MakeRow(("a", "test")) };
        var filters = new Dictionary<string, string> { ["a"] = "   " };
        Assert.Single(ResultFilter.Filter(rows, filters, Identity));
    }

    [Fact]
    public void Filter_NoMatches_ReturnsEmpty()
    {
        var rows = new List<SearchResultRow>
        {
            MakeRow(("name", "Alpha")),
            MakeRow(("name", "Beta")),
        };
        var filters = new Dictionary<string, string> { ["name"] = "Gamma" };
        Assert.Empty(ResultFilter.Filter(rows, filters, Identity));
    }

    [Fact]
    public void Filter_EmptyRows_ReturnsEmpty()
    {
        var filters = new Dictionary<string, string> { ["x"] = "test" };
        Assert.Empty(ResultFilter.Filter([], filters, Identity));
    }

    [Fact]
    public void Filter_NonexistentColumn_ReturnsAll()
    {
        var rows = new List<SearchResultRow> { MakeRow(("a", "val")) };
        // Header resolver returns empty string for unknown key
        var result = ResultFilter.Filter(rows, new Dictionary<string, string> { ["unknown"] = "x" }, _ => "");
        Assert.Single(result); // no header resolved → filter skipped
    }

    [Fact]
    public void Filter_DoesNotMutateInput()
    {
        var rows = new List<SearchResultRow>
        {
            MakeRow(("n", "A")),
            MakeRow(("n", "B")),
        };
        var filters = new Dictionary<string, string> { ["n"] = "A" };
        var result = ResultFilter.Filter(rows, filters, Identity);
        Assert.Single(result);
        Assert.Equal(2, rows.Count); // original unchanged
    }

    [Fact]
    public void Filter_WithHeaderMapping()
    {
        var rows = new List<SearchResultRow>
        {
            MakeRow(("Target Name", "M31")),
            MakeRow(("Target Name", "M33")),
        };
        // Key "targetname" maps to header "Target Name"
        var filters = new Dictionary<string, string> { ["targetname"] = "M31" };
        var result = ResultFilter.Filter(rows, filters, k => k == "targetname" ? "Target Name" : k);
        Assert.Single(result);
    }
}
