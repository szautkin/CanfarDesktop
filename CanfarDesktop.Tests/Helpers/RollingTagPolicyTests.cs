using Xunit;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models.ImageDiscovery;

namespace CanfarDesktop.Tests.Helpers;

public class RollingTagPolicyTests
{
    private static ImageManifest Manifest(string id, DateTimeOffset capturedAt)
        => new() { ImageID = id, CapturedAt = capturedAt, OsFamily = "ubuntu", OsVersion = "22.04", Kernel = "Linux" };

    [Theory]
    [InlineData("images.canfar.net/skaha/astroml:latest", true)]
    [InlineData("images.canfar.net/skaha/astroml:LATEST", true)]
    [InlineData("images.canfar.net/skaha/astroml:dev", true)]
    [InlineData("images.canfar.net/skaha/astroml:nightly", true)]
    [InlineData("images.canfar.net/skaha/astroml:main", true)]
    [InlineData("images.canfar.net/skaha/astroml:edge", true)]
    [InlineData("images.canfar.net/skaha/astroml:24.07", false)]
    [InlineData("images.canfar.net/skaha/casa:6.5.0", false)]
    [InlineData("images.canfar.net/skaha/astroml", false)] // no tag
    public void IsRollingTag_Cases(string id, bool expected)
        => Assert.Equal(expected, RollingTagPolicy.IsRollingTag(id));

    [Fact]
    public void FreshRollingManifest_IsNotStale()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.False(RollingTagPolicy.IsStale(Manifest("x:latest", now), now));
    }

    [Fact]
    public void StaleRollingManifest_IsFlagged()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.True(RollingTagPolicy.IsStale(Manifest("x:latest", now.AddDays(-2)), now));
    }

    [Fact]
    public void VersionedManifest_NeverStale()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.False(RollingTagPolicy.IsStale(Manifest("x:24.07", now.AddDays(-365)), now));
    }

    [Fact]
    public void ExactlyAtBoundary_IsNotStale()
    {
        var now = DateTimeOffset.UtcNow;
        var exact = now - RollingTagPolicy.StalenessWindow;
        Assert.False(RollingTagPolicy.IsStale(Manifest("x:latest", exact), now));
    }

    [Fact]
    public void JustOverBoundary_IsStale()
    {
        var now = DateTimeOffset.UtcNow;
        var over = now - RollingTagPolicy.StalenessWindow - TimeSpan.FromSeconds(1);
        Assert.True(RollingTagPolicy.IsStale(Manifest("x:latest", over), now));
    }

    [Fact]
    public void StaleAgeLabel_NullForFresh()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Null(RollingTagPolicy.StaleAgeLabel(Manifest("x:latest", now), now));
    }

    [Fact]
    public void StaleAgeLabel_DescribesAge()
    {
        var now = DateTimeOffset.UtcNow;
        var label = RollingTagPolicy.StaleAgeLabel(Manifest("x:latest", now.AddDays(-3)), now);
        Assert.NotNull(label);
        Assert.Contains("3", label);
        Assert.Contains("rediscover", label, StringComparison.OrdinalIgnoreCase);
    }
}
