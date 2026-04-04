using Xunit;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;

namespace CanfarDesktop.Tests.Helpers;

public class ResultSorterTests
{
    private static SearchResultRow MakeRow(params (string key, string value)[] values)
    {
        var row = new SearchResultRow();
        foreach (var (k, v) in values) row.Values[k] = v;
        return row;
    }

    [Fact]
    public void Sort_StringColumn_Ascending()
    {
        var rows = new List<SearchResultRow>
        {
            MakeRow(("name", "Gamma")),
            MakeRow(("name", "Alpha")),
            MakeRow(("name", "Beta")),
        };
        var sorted = ResultSorter.Sort(rows, "name", ascending: true);
        Assert.Equal("Alpha", sorted[0].Get("name"));
        Assert.Equal("Beta", sorted[1].Get("name"));
        Assert.Equal("Gamma", sorted[2].Get("name"));
    }

    [Fact]
    public void Sort_StringColumn_Descending()
    {
        var rows = new List<SearchResultRow>
        {
            MakeRow(("name", "Alpha")),
            MakeRow(("name", "Gamma")),
            MakeRow(("name", "Beta")),
        };
        var sorted = ResultSorter.Sort(rows, "name", ascending: false);
        Assert.Equal("Gamma", sorted[0].Get("name"));
        Assert.Equal("Beta", sorted[1].Get("name"));
        Assert.Equal("Alpha", sorted[2].Get("name"));
    }

    [Fact]
    public void Sort_NumericColumn_SortsNumerically()
    {
        var rows = new List<SearchResultRow>
        {
            MakeRow(("ra", "100.5")),
            MakeRow(("ra", "10.2")),
            MakeRow(("ra", "99.9")),
        };
        var sorted = ResultSorter.Sort(rows, "ra", ascending: true);
        Assert.Equal("10.2", sorted[0].Get("ra"));
        Assert.Equal("99.9", sorted[1].Get("ra"));
        Assert.Equal("100.5", sorted[2].Get("ra"));
    }

    [Fact]
    public void Sort_NumericColumn_NotLexicographic()
    {
        // "9" > "10" lexicographically, but 9 < 10 numerically
        var rows = new List<SearchResultRow>
        {
            MakeRow(("val", "9")),
            MakeRow(("val", "10")),
            MakeRow(("val", "2")),
        };
        var sorted = ResultSorter.Sort(rows, "val", ascending: true);
        Assert.Equal("2", sorted[0].Get("val"));
        Assert.Equal("9", sorted[1].Get("val"));
        Assert.Equal("10", sorted[2].Get("val"));
    }

    [Fact]
    public void Sort_MixedNumericString_FallsBackToString()
    {
        var rows = new List<SearchResultRow>
        {
            MakeRow(("col", "abc")),
            MakeRow(("col", "123")),
            MakeRow(("col", "def")),
        };
        // Can't parse "abc" as number → string comparison for all
        var sorted = ResultSorter.Sort(rows, "col", ascending: true);
        Assert.Equal("123", sorted[0].Get("col")); // "1" < "a" in string comparison
    }

    [Fact]
    public void Sort_EmptyValues_SortLast()
    {
        var rows = new List<SearchResultRow>
        {
            MakeRow(("col", "")),
            MakeRow(("col", "Alpha")),
            MakeRow(("col", "Beta")),
        };
        var sorted = ResultSorter.Sort(rows, "col", ascending: true);
        Assert.Equal("Alpha", sorted[0].Get("col"));
        Assert.Equal("Beta", sorted[1].Get("col"));
        Assert.Equal("", sorted[2].Get("col")); // empty last
    }

    [Fact]
    public void Sort_EmptyList_ReturnsEmpty()
    {
        var sorted = ResultSorter.Sort([], "col", ascending: true);
        Assert.Empty(sorted);
    }

    [Fact]
    public void Sort_NullColumnHeader_ReturnsCopy()
    {
        var rows = new List<SearchResultRow> { MakeRow(("a", "1")) };
        var sorted = ResultSorter.Sort(rows, "", ascending: true);
        Assert.Single(sorted);
    }

    [Fact]
    public void Sort_DoesNotMutateInput()
    {
        var rows = new List<SearchResultRow>
        {
            MakeRow(("n", "B")),
            MakeRow(("n", "A")),
        };
        var sorted = ResultSorter.Sort(rows, "n", ascending: true);
        Assert.Equal("B", rows[0].Get("n")); // original unchanged
        Assert.Equal("A", sorted[0].Get("n")); // sorted copy
    }

    [Fact]
    public void SmartCompare_BothEmpty_ReturnsZero()
    {
        Assert.Equal(0, ResultSorter.SmartCompare("", ""));
        Assert.Equal(0, ResultSorter.SmartCompare("  ", "  "));
    }

    [Fact]
    public void SmartCompare_ScientificNotation()
    {
        Assert.True(ResultSorter.SmartCompare("1.5E-7", "2.0E-7") < 0);
    }
}
