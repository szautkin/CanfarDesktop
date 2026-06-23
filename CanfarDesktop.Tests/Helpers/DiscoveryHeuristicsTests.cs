using Xunit;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models.ImageDiscovery;

namespace CanfarDesktop.Tests.Helpers;

public class DiscoveryHeuristicsTests
{
    [Fact]
    public void MakeJobName_IsDns1123Safe()
    {
        var name = DiscoveryHeuristics.MakeJobName("vp", "images.canfar.net/skaha/astroml:24.07", "abcd1234");
        Assert.StartsWith("vp-", name);
        Assert.EndsWith("-abcd1234", name);
        Assert.True(name.Length <= 63);
        Assert.DoesNotContain("--", name);
        Assert.False(name.StartsWith('-') || name.EndsWith('-'));
        Assert.All(name, c => Assert.True(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'));
    }

    [Fact]
    public void MakeJobName_CapsLongImageIds()
    {
        var huge = "images.canfar.net/" + new string('a', 200) + ":latest";
        Assert.True(DiscoveryHeuristics.MakeJobName("vi", huge, "deadbeef").Length <= 63);
    }

    [Fact]
    public void MakeJobName_DegenerateInput_FallsBackToPrefixSuffix()
        => Assert.Equal("vp-abcd1234", DiscoveryHeuristics.MakeJobName("vp", "::::", "abcd1234"));

    [Fact]
    public void Strategy_HeadlessIsInTarget_OthersInspector()
    {
        Assert.Equal(ProbeStrategy.InTarget, DiscoveryHeuristics.Strategy(new[] { "headless", "notebook" }));
        Assert.Equal(ProbeStrategy.Inspector, DiscoveryHeuristics.Strategy(new[] { "notebook", "desktop" }));
        Assert.Equal(ProbeStrategy.Inspector, DiscoveryHeuristics.Strategy(null));
    }

    [Fact]
    public void IsStubManifest_Cases()
    {
        Assert.True(DiscoveryHeuristics.IsStubManifest(
            new ImageManifest { ImageID = "x:1", ProbeNotes = "syft failed" }));
        Assert.False(DiscoveryHeuristics.IsStubManifest(
            new ImageManifest { ImageID = "x:1", ProbeNotes = "note", ApkPackages = new[] { new ImagePackage("musl", "1") } }));
        Assert.False(DiscoveryHeuristics.IsStubManifest(
            new ImageManifest { ImageID = "x:1" })); // empty + no notes → not a stub
    }

    [Theory]
    [InlineData("jobs.batch \"foo\" not found", true)]
    [InlineData("HTTP 500: jobs.batch not found", true)]
    [InlineData("quota exceeded", false)]
    [InlineData("HTTP 400 bad request", false)]
    public void IsSkahaJobNotFoundRace_Cases(string message, bool expected)
        => Assert.Equal(expected, DiscoveryHeuristics.IsSkahaJobNotFoundRace(message));

    [Fact]
    public void RaceBackoffs_Are_3_7_15()
        => Assert.Equal(new[] { 3, 7, 15 }, DiscoveryHeuristics.SkahaRaceBackoffsSeconds);
}
