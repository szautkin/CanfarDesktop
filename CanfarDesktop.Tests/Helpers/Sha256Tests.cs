using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

public class Sha256Tests
{
    [Fact]
    public void HexOf_KnownVector()
        => Assert.Equal("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824", Sha256.HexOf("hello"));

    [Fact]
    public void ShortHexOf_Is12CharsAndStable()
    {
        Assert.Equal("2cf24dba5fb0", Sha256.ShortHexOf("hello"));
        Assert.Equal(12, Sha256.ShortHexOf("any script body").Length);
        Assert.Equal(Sha256.ShortHexOf("same"), Sha256.ShortHexOf("same"));
    }
}
