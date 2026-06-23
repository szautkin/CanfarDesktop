using Xunit;
using CanfarDesktop.Helpers.ImageDiscovery;
using CanfarDesktop.Models.ImageDiscovery;

namespace CanfarDesktop.Tests.Services.ImageDiscovery;

public class DiscoveryFormattingTests
{
    [Theory]
    [InlineData(FailureCategory.JobSubmitFailed, "Submit failed")]
    [InlineData(FailureCategory.JobTimedOut, "Timed out")]
    [InlineData(FailureCategory.ManifestFetchFailed, "No manifest")]
    [InlineData(FailureCategory.ManifestParseFailed, "Bad manifest")]
    [InlineData(FailureCategory.Cancelled, "Cancelled")]
    [InlineData(FailureCategory.Unknown, "Failed")]
    public void CategoryLabel_MatchesMacOs(FailureCategory category, string expected)
        => Assert.Equal(expected, DiscoveryFormatting.CategoryLabel(category));

    [Fact]
    public void TimeAgo_BucketsByElapsed()
    {
        var now = new DateTimeOffset(2026, 6, 23, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal("just now", DiscoveryFormatting.TimeAgo(now.AddSeconds(-5), now));
        Assert.Equal("just now", DiscoveryFormatting.TimeAgo(now.AddSeconds(10), now)); // future/skew
        Assert.Equal("45s ago", DiscoveryFormatting.TimeAgo(now.AddSeconds(-45), now));
        Assert.Equal("5m ago", DiscoveryFormatting.TimeAgo(now.AddMinutes(-5), now));
        Assert.Equal("3h ago", DiscoveryFormatting.TimeAgo(now.AddHours(-3), now));
        Assert.Equal("2d ago", DiscoveryFormatting.TimeAgo(now.AddDays(-2), now));
    }

    [Fact]
    public void TimeAgo_BeyondTwoWeeks_FallsBackToAbsoluteDate()
    {
        var now = new DateTimeOffset(2026, 6, 23, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal("May 14", DiscoveryFormatting.TimeAgo(new DateTimeOffset(2026, 5, 14, 0, 0, 0, TimeSpan.Zero), now));
    }

    [Fact]
    public void IsLikelyStillRecovering_OnlyTimedOutWithinTenMinutes()
    {
        var now = new DateTimeOffset(2026, 6, 23, 12, 0, 0, TimeSpan.Zero);
        Assert.True(DiscoveryFormatting.IsLikelyStillRecovering(FailureCategory.JobTimedOut, now.AddMinutes(-5), now));
        Assert.False(DiscoveryFormatting.IsLikelyStillRecovering(FailureCategory.JobTimedOut, now.AddMinutes(-15), now));
        Assert.False(DiscoveryFormatting.IsLikelyStillRecovering(FailureCategory.ManifestFetchFailed, now.AddMinutes(-1), now));
    }
}
