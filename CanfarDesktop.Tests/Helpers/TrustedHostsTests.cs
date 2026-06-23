using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

public class TrustedHostsTests
{
    [Theory]
    [InlineData("https://ws-uv.canfar.net/skaha/v1/session", true)]
    [InlineData("https://ws-cadc.canfar.net/ac/whoami", true)]
    [InlineData("https://canfar.net/", true)]
    [InlineData("https://ws.cadc-ccda.hia-iha.nrc-cnrc.gc.ca/argus/sync", true)]
    [InlineData("http://ws-uv.canfar.net/skaha", false)]      // not https
    [InlineData("ftp://ws-uv.canfar.net/x", false)]           // not https
    [InlineData("https://canfar.net.evil.com/x", false)]      // lookalike suffix
    [InlineData("https://evilcanfar.net/x", false)]           // no dot boundary
    [InlineData("https://example.com/data.fits", false)]      // untrusted host
    public void IsTrusted_Cases(string url, bool expected)
        => Assert.Equal(expected, TrustedHosts.IsTrusted(new Uri(url)));

    [Fact]
    public void IsTrusted_Null_ReturnsFalse()
        => Assert.False(TrustedHosts.IsTrusted(null));
}
