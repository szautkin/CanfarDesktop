using Xunit;
using CanfarDesktop.Models.ImageDiscovery;

namespace CanfarDesktop.Tests.Models;

public class PackageQueryTests
{
    private static ImageManifest Manifest() => new()
    {
        ImageID = "img:1",
        OsFamily = "ubuntu",
        OsVersion = "22.04",
        DpkgPackages = new[] { new ImagePackage("libc6", "1"), new ImagePackage("openssh-client", "1") },
        PythonPackages = new[] { new PythonPackage("astropy", "6", "pip", "base"), new PythonPackage("numpy", "1", "pip", "base") },
        Capabilities = new[] { "fitsio" },
    };

    [Fact]
    public void IsEmpty_Cases()
    {
        Assert.True(new PackageQuery().IsEmpty);
        Assert.False(new PackageQuery { Python = { "astropy" } }.IsEmpty);
    }

    [Fact]
    public void Matches_RequiresEveryConstraint()
    {
        var m = Manifest();
        Assert.True(new PackageQuery { Dpkg = { "libc6" }, Python = { "astropy" } }.Matches(m));
        Assert.True(new PackageQuery { OsFamilies = { "ubuntu" }, Capabilities = { "fitsio" } }.Matches(m));
        Assert.False(new PackageQuery { Dpkg = { "libc6", "does-not-exist" } }.Matches(m));
        Assert.False(new PackageQuery { OsFamilies = { "almalinux" } }.Matches(m));
        Assert.False(new PackageQuery { Python = { "astropy" }, Capabilities = { "gpu" } }.Matches(m)); // missing gpu
    }

    [Fact]
    public void EmptyQuery_MatchesAndScoresOne()
    {
        var m = Manifest();
        Assert.True(new PackageQuery().Matches(m));
        var (score, missing) = new PackageQuery().Score(m);
        Assert.Equal(1.0, score);
        Assert.Empty(missing);
    }

    [Fact]
    public void Score_ReportsPartialCoverageAndMissing()
    {
        var m = Manifest();
        var query = new PackageQuery { Python = { "astropy", "scipy" } }; // 1 of 2 present
        var (score, missing) = query.Score(m);
        Assert.Equal(0.5, score, 3);
        Assert.Contains("python:scipy", missing);
        Assert.DoesNotContain("python:astropy", missing);
    }
}
