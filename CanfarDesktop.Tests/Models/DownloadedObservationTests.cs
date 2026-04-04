using Xunit;
using CanfarDesktop.Models;

namespace CanfarDesktop.Tests.Models;

public class DownloadedObservationTests
{
    [Fact]
    public void FileExists_ReturnsFalse_WhenPathEmpty()
    {
        var obs = new DownloadedObservation { LocalPath = "" };
        Assert.False(obs.FileExists);
    }

    [Fact]
    public void FileExists_ReturnsFalse_WhenPathDoesNotExist()
    {
        var obs = new DownloadedObservation { LocalPath = @"C:\nonexistent\fake.fits" };
        Assert.False(obs.FileExists);
    }

    [Fact]
    public void Filename_ReturnsEmpty_WhenPathEmpty()
    {
        var obs = new DownloadedObservation { LocalPath = "" };
        Assert.Equal("", obs.Filename);
    }

    [Fact]
    public void Filename_ExtractsFromPath()
    {
        var obs = new DownloadedObservation { LocalPath = @"C:\data\observation.fits" };
        Assert.Equal("observation.fits", obs.Filename);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData(500L, "500 B")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(1572864L, "1.5 MB")]
    [InlineData(1610612736L, "1.50 GB")]
    public void FormattedSize_FormatsCorrectly(long? size, string expected)
    {
        var obs = new DownloadedObservation { FileSize = size };
        Assert.Equal(expected, obs.FormattedSize);
    }

    [Fact]
    public void FromSearchResult_HandlesEmptyRow()
    {
        var row = new SearchResultRow();
        var obs = DownloadedObservation.FromSearchResult(row, "/tmp/test.fits", null,
            _ => "nonexistent_header");

        Assert.Equal("/tmp/test.fits", obs.LocalPath);
        Assert.Equal("", obs.PublisherID);
        Assert.Equal("", obs.Collection);
    }

    [Fact]
    public void FromSearchResult_ExtractsDataLinkUrls()
    {
        var row = new SearchResultRow();
        var dataLink = new DataLinkResult
        {
            Thumbnails = ["https://example.com/thumb.jpg"],
            Previews = ["https://example.com/preview.jpg"]
        };

        var obs = DownloadedObservation.FromSearchResult(row, "/path", dataLink, _ => "x");

        Assert.Equal("https://example.com/thumb.jpg", obs.ThumbnailURL);
        Assert.Equal("https://example.com/preview.jpg", obs.PreviewURL);
    }

    [Fact]
    public void FromSearchResult_NullDataLink_NoUrls()
    {
        var row = new SearchResultRow();
        var obs = DownloadedObservation.FromSearchResult(row, "/path", null, _ => "x");

        Assert.Null(obs.ThumbnailURL);
        Assert.Null(obs.PreviewURL);
    }

    [Fact]
    public void FromSearchResult_NullLocalPath_DefaultsToEmpty()
    {
        var row = new SearchResultRow();
        var obs = DownloadedObservation.FromSearchResult(row, null!, null, _ => "x");
        Assert.Equal("", obs.LocalPath);
    }

    [Fact]
    public void DefaultValues_AreSet()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var obs = new DownloadedObservation();

        Assert.NotEmpty(obs.Id);
        Assert.Equal("", obs.PublisherID);
        Assert.Equal("", obs.LocalPath);
        Assert.Null(obs.FileSize);
        Assert.True(obs.DownloadedAt >= before);
        Assert.True(obs.DownloadedAt <= DateTime.UtcNow.AddSeconds(1));
    }

    [Fact]
    public void TwoInstances_HaveDifferentIds()
    {
        var a = new DownloadedObservation();
        var b = new DownloadedObservation();
        Assert.NotEqual(a.Id, b.Id);
    }

    [Theory]
    [InlineData(1024L, "1.0 KB")]         // exact KB boundary
    [InlineData(1048576L, "1.0 MB")]      // exact MB boundary
    [InlineData(1073741824L, "1.00 GB")]  // exact GB boundary
    public void FormattedSize_ExactBoundaries(long size, string expected)
    {
        var obs = new DownloadedObservation { FileSize = size };
        Assert.Equal(expected, obs.FormattedSize);
    }

    [Fact]
    public void FromSearchResult_EmptyDataLinkLists_NullUrls()
    {
        var row = new SearchResultRow();
        var emptyDataLink = new DataLinkResult { Thumbnails = [], Previews = [] };
        var obs = DownloadedObservation.FromSearchResult(row, "/p", emptyDataLink, _ => "x");

        Assert.Null(obs.ThumbnailURL);
        Assert.Null(obs.PreviewURL);
    }

    [Fact]
    public void FileExists_ReturnsFalse_WhenPathIsWhitespace()
    {
        var obs = new DownloadedObservation { LocalPath = "   " };
        Assert.False(obs.FileExists);
    }
}
