using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

public class FtsQueryTests
{
    [Fact]
    public void Empty_ReturnsEmpty()
    {
        Assert.Equal("", FtsQuery.BuildPrefix(null));
        Assert.Equal("", FtsQuery.BuildPrefix("   "));
    }

    [Fact]
    public void Tokens_BecomeQuotedPrefixTerms()
        => Assert.Equal("\"galaxy\"* \"m3\"*", FtsQuery.BuildPrefix("galaxy m3"));

    [Fact]
    public void DoublesInternalQuotes_ToNeutralizeOperators()
        => Assert.Equal("\"a\"\"b\"*", FtsQuery.BuildPrefix("a\"b"));

    [Fact]
    public void DropsTokensWithoutLettersOrDigits()
    {
        // Pure-operator input would otherwise form empty FTS phrases.
        Assert.Equal("", FtsQuery.BuildPrefix("! * ( )"));
        Assert.Equal("\"ok\"*", FtsQuery.BuildPrefix("ok *"));
    }
}
