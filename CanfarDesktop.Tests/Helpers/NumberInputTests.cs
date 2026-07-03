using System.Globalization;
using CanfarDesktop.Helpers;
using Xunit;

namespace CanfarDesktop.Tests.Helpers;

public class NumberInputTests
{
    [Fact]
    public void Wire_IsStrictInvariant()
    {
        Assert.True(NumberInput.TryParseWire("298.443", out var v));
        Assert.Equal(298.443, v, 6);
        Assert.False(NumberInput.TryParseWire("298,443", out _)); // wire data is never comma-decimal
        Assert.False(NumberInput.TryParseWire("", out _));
        Assert.False(NumberInput.TryParseWire(null, out _));
    }

    [Fact]
    public void User_AcceptsDotAndSingleComma()
    {
        Assert.True(NumberInput.TryParseUser("0.5", out var dot));
        Assert.True(NumberInput.TryParseUser("0,5", out var comma));
        Assert.Equal(dot, comma, 10);
        Assert.False(NumberInput.TryParseUser("1,234,5", out _)); // ambiguous thousands-style
    }

    [Fact]
    public void Parsing_IsIdentical_UnderFrenchCulture()
    {
        // The class of bug this guards: choosing Français must not change how the app reads
        // dot-decimal wire data or user-typed numbers.
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
            Assert.True(NumberInput.TryParseWire("298.443", out var wire));
            Assert.Equal(298.443, wire, 6);
            Assert.True(NumberInput.TryParseUser("0.5", out var u1));
            Assert.True(NumberInput.TryParseUser("0,5", out var u2));
            Assert.Equal(0.5, u1, 10);
            Assert.Equal(0.5, u2, 10);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

}
