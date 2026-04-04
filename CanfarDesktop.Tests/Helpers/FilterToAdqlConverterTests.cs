using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

public class FilterToAdqlConverterTests
{
    [Fact]
    public void ConvertFilters_StringFilter_GeneratesLike()
    {
        var filters = new Dictionary<string, string> { ["collection"] = "JWST" };
        var result = FilterToAdqlConverter.ConvertFilters(filters);
        Assert.NotNull(result);
        Assert.Contains("lower(Observation.collection) LIKE '%jwst%'", result);
    }

    [Fact]
    public void ConvertFilters_NumericFilter_GeneratesEquals()
    {
        var filters = new Dictionary<string, string> { ["inttime"] = "3600" };
        var result = FilterToAdqlConverter.ConvertFilters(filters);
        Assert.NotNull(result);
        Assert.Contains("Plane.time_exposure = 3600", result);
    }

    [Fact]
    public void ConvertFilters_MultipleFilters_JoinedWithAnd()
    {
        var filters = new Dictionary<string, string>
        {
            ["collection"] = "CFHT",
            ["targetname"] = "M31"
        };
        var result = FilterToAdqlConverter.ConvertFilters(filters);
        Assert.NotNull(result);
        Assert.Contains("AND", result);
        Assert.Contains("Observation.collection", result);
        Assert.Contains("Observation.target_name", result);
    }

    [Fact]
    public void ConvertFilters_EmptyFilters_ReturnsNull()
    {
        Assert.Null(FilterToAdqlConverter.ConvertFilters(new Dictionary<string, string>()));
    }

    [Fact]
    public void ConvertFilters_WhitespaceFilter_Ignored()
    {
        var filters = new Dictionary<string, string> { ["collection"] = "   " };
        Assert.Null(FilterToAdqlConverter.ConvertFilters(filters));
    }

    [Fact]
    public void ConvertFilters_UnknownColumn_Ignored()
    {
        var filters = new Dictionary<string, string> { ["download"] = "test" };
        Assert.Null(FilterToAdqlConverter.ConvertFilters(filters));
    }

    [Fact]
    public void ConvertFilters_SingleQuoteEscaped()
    {
        var filters = new Dictionary<string, string> { ["targetname"] = "O'Brien" };
        var result = FilterToAdqlConverter.ConvertFilters(filters);
        Assert.NotNull(result);
        Assert.Contains("o''brien", result);
        Assert.DoesNotContain("O'Brien", result);
    }

    [Fact]
    public void ConvertFilters_WildcardsEscaped()
    {
        var filters = new Dictionary<string, string> { ["targetname"] = "100%_done" };
        var result = FilterToAdqlConverter.ConvertFilters(filters);
        Assert.NotNull(result);
        Assert.Contains("\\%", result);
        Assert.Contains("\\_", result);
    }

    [Fact]
    public void ConvertFilters_ComputedColumn_UsesFunction()
    {
        var filters = new Dictionary<string, string> { ["ra(j20000)"] = "180.5" };
        var result = FilterToAdqlConverter.ConvertFilters(filters);
        Assert.NotNull(result);
        Assert.Contains("COORD1(CENTROID(Plane.position_bounds)) = 180.5", result);
    }

    [Fact]
    public void AppendToQuery_AddsToExistingWhere()
    {
        var baseAdql = "SELECT * FROM tbl WHERE x = 1";
        var filters = new Dictionary<string, string> { ["collection"] = "JWST" };
        var result = FilterToAdqlConverter.AppendToQuery(baseAdql, filters);
        Assert.Contains("WHERE x = 1", result);
        Assert.Contains("AND lower(Observation.collection) LIKE '%jwst%'", result);
    }

    [Fact]
    public void AppendToQuery_NoFilters_ReturnsOriginal()
    {
        var baseAdql = "SELECT * FROM tbl WHERE x = 1";
        var result = FilterToAdqlConverter.AppendToQuery(baseAdql, new Dictionary<string, string>());
        Assert.Equal(baseAdql, result);
    }

    [Fact]
    public void ConvertFilters_DecimalNumber_NumericEquals()
    {
        var filters = new Dictionary<string, string> { ["inttime"] = "0.5" };
        var result = FilterToAdqlConverter.ConvertFilters(filters);
        Assert.NotNull(result);
        Assert.Contains("Plane.time_exposure = 0.5", result);
    }

    [Fact]
    public void ConvertFilters_NonNumericString_UsesLike()
    {
        var filters = new Dictionary<string, string> { ["instrument"] = "WFC3" };
        var result = FilterToAdqlConverter.ConvertFilters(filters);
        Assert.NotNull(result);
        Assert.Contains("LIKE '%wfc3%'", result);
    }
}
