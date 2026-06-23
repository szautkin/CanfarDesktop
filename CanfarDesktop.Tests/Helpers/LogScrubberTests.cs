using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

public class LogScrubberTests
{
    [Fact]
    public void Scrub_RedactsStandaloneBearerToken()
    {
        var s = LogScrubber.Scrub("request failed with Bearer eyJabc.def-ghi_123 attached");
        Assert.DoesNotContain("eyJabc.def-ghi_123", s);
        Assert.Contains("<redacted>", s);
    }

    [Fact]
    public void Scrub_RedactsAuthorizationHeaderValue()
    {
        var s = LogScrubber.Scrub("Authorization: Bearer secrettoken12345");
        Assert.DoesNotContain("secrettoken12345", s);
        Assert.Contains("<redacted>", s);
    }

    [Fact]
    public void Scrub_LeavesNormalTextUnchanged()
    {
        const string text = "normal log line without secrets";
        Assert.Equal(text, LogScrubber.Scrub(text));
    }
}
