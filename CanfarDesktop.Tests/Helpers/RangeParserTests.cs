using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

public class RangeParserTests
{
    [Fact]
    public void TryParse_Range_ReturnsBetween()
    {
        Assert.True(RangeParser.TryParse("2020..2021", out var r));
        Assert.Equal(RangeOperand.Between, r!.Operand);
        Assert.Equal("2020", r.Value1);
        Assert.Equal("2021", r.Value2);
    }

    [Fact]
    public void TryParse_RangeWithSpaces_TrimsValues()
    {
        Assert.True(RangeParser.TryParse(" 100 .. 500 ", out var r));
        Assert.Equal(RangeOperand.Between, r!.Operand);
        Assert.Equal("100", r.Value1);
        Assert.Equal("500", r.Value2);
    }

    [Fact]
    public void TryParse_GreaterThan_ReturnsCorrectOperand()
    {
        Assert.True(RangeParser.TryParse("> 5.0", out var r));
        Assert.Equal(RangeOperand.GreaterThan, r!.Operand);
        Assert.Equal("5.0", r.Value1);
    }

    [Fact]
    public void TryParse_GreaterThanOrEqual_ReturnsCorrectOperand()
    {
        Assert.True(RangeParser.TryParse(">= 100", out var r));
        Assert.Equal(RangeOperand.GreaterThanOrEqual, r!.Operand);
        Assert.Equal("100", r.Value1);
    }

    [Fact]
    public void TryParse_LessThan_ReturnsCorrectOperand()
    {
        Assert.True(RangeParser.TryParse("< 50", out var r));
        Assert.Equal(RangeOperand.LessThan, r!.Operand);
        Assert.Equal("50", r.Value1);
    }

    [Fact]
    public void TryParse_LessThanOrEqual_ReturnsCorrectOperand()
    {
        Assert.True(RangeParser.TryParse("<= 100", out var r));
        Assert.Equal(RangeOperand.LessThanOrEqual, r!.Operand);
        Assert.Equal("100", r.Value1);
    }

    [Fact]
    public void TryParse_PlainValue_ReturnsEquals()
    {
        Assert.True(RangeParser.TryParse("42", out var r));
        Assert.Equal(RangeOperand.Equals, r!.Operand);
        Assert.Equal("42", r.Value1);
        Assert.Null(r.Value2);
    }

    [Fact]
    public void TryParse_NullInput_ReturnsFalse()
    {
        Assert.False(RangeParser.TryParse(null, out var r));
        Assert.Null(r);
    }

    [Fact]
    public void TryParse_WhitespaceInput_ReturnsFalse()
    {
        Assert.False(RangeParser.TryParse("   ", out var r));
        Assert.Null(r);
    }

    [Fact]
    public void TryParse_DateRange_ParsesCorrectly()
    {
        Assert.True(RangeParser.TryParse("2018-09-22..2018-09-23", out var r));
        Assert.Equal(RangeOperand.Between, r!.Operand);
        Assert.Equal("2018-09-22", r.Value1);
        Assert.Equal("2018-09-23", r.Value2);
    }

    [Fact]
    public void TryParse_RangeWithUnits_PreservesUnits()
    {
        Assert.True(RangeParser.TryParse("400..700nm", out var r));
        Assert.Equal(RangeOperand.Between, r!.Operand);
        Assert.Equal("400", r.Value1);
        Assert.Equal("700nm", r.Value2);
    }
}
