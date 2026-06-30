using Xunit;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>SCI-5: download_observation can target a specific DataLink artifact by index. Locks the
/// selection + bounds contract that decides which file actually gets downloaded.</summary>
public class DataLinkArtifactSelectorTests
{
    private static DataLinkResult Links(params string[] urls) => new()
    {
        DirectFiles = urls.Select(u => new DataLinkFile { Url = u }).ToList(),
    };

    [Fact]
    public void SelectUrl_ExplicitIndex_PicksThatArtifact()
    {
        var links = Links("https://a/cube.fits", "https://a/mom0.fits", "https://a/spec.fits");
        Assert.Equal("https://a/mom0.fits", DataLinkArtifactSelector.SelectUrl(links, 1, "https://fallback"));
        Assert.Equal("https://a/spec.fits", DataLinkArtifactSelector.SelectUrl(links, 2, "https://fallback"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]   // exactly past the end (0..2 valid)
    [InlineData(99)]
    public void SelectUrl_IndexOutOfRange_Throws(int badIndex)
    {
        var links = Links("https://a/0", "https://a/1", "https://a/2");
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => DataLinkArtifactSelector.SelectUrl(links, badIndex, "https://fallback"));
        Assert.Contains("list_observation_artifacts", ex.Message); // actionable: tells the caller what to do
    }

    [Fact]
    public void SelectUrl_NoIndex_PrefersFirstDirectFile()
    {
        var links = Links("https://a/primary.fits", "https://a/other.fits");
        Assert.Equal("https://a/primary.fits", DataLinkArtifactSelector.SelectUrl(links, null, "https://fallback"));
    }

    [Fact]
    public void SelectUrl_NoIndex_NoDirectFiles_UsesFallback()
    {
        Assert.Equal("https://fallback", DataLinkArtifactSelector.SelectUrl(new DataLinkResult(), null, "https://fallback"));
    }

    [Fact]
    public void SelectUrl_IndexIntoEmpty_Throws()
    {
        // index 0 is out of range when there are no direct files at all.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DataLinkArtifactSelector.SelectUrl(new DataLinkResult(), 0, "https://fallback"));
    }
}
