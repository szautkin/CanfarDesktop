using System.Net;
using Xunit;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Services;
using CanfarDesktop.Tests.Helpers;

namespace CanfarDesktop.Tests.Services;

/// <summary>
/// Observation downloads used to live and die with the screen that started them: closing Research's
/// detail mid-download threw after the bytes had landed, so the record never heard, and choosing
/// another observation recorded the file on THAT one. They belong to the app now, report to the
/// status bar, and record against the observation they were started for.
/// </summary>
[Collection("TaskRegistry")] // the status bar's registry is shared, so these do not run beside its tests
public class ObservationDownloaderTests : IDisposable
{
    private const string Url = "https://archive.test/data/obs.fits";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "verbinal-downloader-" + Guid.NewGuid().ToString("N")[..8]);

    public ObservationDownloaderTests()
    {
        Directory.CreateDirectory(_dir);
        TaskRegistry.ResetForTests();
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        TaskRegistry.ResetForTests();
        GC.SuppressFinalize(this);
    }

    private string PathFor(string name) => Path.Combine(_dir, name);

    private static HttpResponseMessage File(int bytes)
        => new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[bytes]) };

    private static (ObservationDownloader Downloader, ObservationStore Store) Make(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> server)
    {
        var store = new ObservationStore(); // no packaged app here, so it keeps its records in memory
        var downloads = new ObservationDownloadService(
            new DataLinkService(new HttpClient(new MockHttpMessageHandler(server)), new ApiEndpoints()));
        return (new ObservationDownloader(() => downloads, store), store);
    }

    private static TrackedTask OnlyTask() => Assert.Single(TaskRegistry.Snapshot());

    // ── What it records ──────────────────────────────────────────────────────

    [Fact]
    public async Task ADownload_RecordsItsObservation_WithTheFile()
    {
        var (downloader, store) = Make(_ => Task.FromResult(File(2048)));
        var path = PathFor("m31.fits");
        var record = new DownloadedObservation { PublisherID = "ivo://cadc/M31", TargetName = "M31" };

        await downloader.Start(new ObservationDownloadRequest("ivo://cadc/M31", path, record, Url: Url));

        var saved = Assert.Single(store.Observations);
        Assert.Equal("M31", saved.TargetName);
        Assert.Equal(path, saved.LocalPath);
        Assert.Equal(2048, saved.FileSize);
    }

    /// <summary>
    /// The regression: each download records the observation it was STARTED for. Research used to
    /// write onto whatever was selected when the bytes arrived.
    /// </summary>
    [Fact]
    public async Task TwoDownloads_EachRecordTheirOwnObservation()
    {
        var (downloader, store) = Make(_ => Task.FromResult(File(16)));
        var a = new DownloadedObservation { PublisherID = "ivo://cadc/A" };
        var b = new DownloadedObservation { PublisherID = "ivo://cadc/B" };

        await Task.WhenAll(
            downloader.Start(new ObservationDownloadRequest("ivo://cadc/A", PathFor("a.fits"), a, Url: Url)),
            downloader.Start(new ObservationDownloadRequest("ivo://cadc/B", PathFor("b.fits"), b, Url: Url)));

        Assert.Equal(PathFor("a.fits"), store.Find("ivo://cadc/A")!.LocalPath);
        Assert.Equal(PathFor("b.fits"), store.Find("ivo://cadc/B")!.LocalPath);
    }

    /// <summary>A plain save, which Research does not track, leaves Research alone.</summary>
    [Fact]
    public async Task APlainSave_WithoutARecord_RecordsNothing()
    {
        var (downloader, store) = Make(_ => Task.FromResult(File(16)));

        await downloader.Start(new ObservationDownloadRequest("ivo://cadc/X", PathFor("x.fits"), Url: Url));

        Assert.True(System.IO.File.Exists(PathFor("x.fits")));
        Assert.Empty(store.Observations);
    }

    // ── What the status bar sees ─────────────────────────────────────────────

    [Fact]
    public async Task ADownload_IsInTheStatusBar_AndFinishesThere()
    {
        var (downloader, _) = Make(_ => Task.FromResult(File(16)));

        await downloader.Start(new ObservationDownloadRequest("ivo://cadc/M31", PathFor("m31.fits"), Url: Url));

        var task = OnlyTask();
        Assert.Equal(TaskKind.Download, task.Kind);
        Assert.Equal("Download m31.fits", task.Label);
        Assert.Equal(TaskProgress.Succeeded, task.Progress);
    }

    /// <summary>
    /// A failure is the status bar's to show — a screen that started it may be long gone — and it
    /// records nothing, so Research does not claim a file that is not there.
    /// </summary>
    [Fact]
    public async Task AFailure_IsShownWithItsReason_AndRecordsNothing()
    {
        var (downloader, store) = Make(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        var record = new DownloadedObservation { PublisherID = "ivo://cadc/gone" };

        await Assert.ThrowsAnyAsync<HttpRequestException>(() =>
            downloader.Start(new ObservationDownloadRequest("ivo://cadc/gone", PathFor("gone.fits"), record, Url: Url)));

        var task = OnlyTask();
        Assert.Equal(TaskProgress.Failed, task.Progress);
        Assert.False(string.IsNullOrWhiteSpace(task.Message));
        Assert.Empty(store.Observations);
    }

    [Theory]
    [InlineData(512L * 1024, 2048L * 1024, "512 KB of 2 MB · 25")]
    [InlineData(3L * 1024 * 1024, null, "3 MB so far")]
    public void Progress_ReadsAsBytesOfTheTotal_OrSoFarWithoutOne(long downloaded, long? total, string expected)
        => Assert.StartsWith(expected, ObservationDownloader.Describe(downloaded, total));

    // ── While it runs ────────────────────────────────────────────────────────

    /// <summary>
    /// Asking again for a file already on its way is the same download, not a second: two writers of
    /// one file would each tear down the other's temporary copy.
    /// </summary>
    [Fact]
    public async Task AskingTwiceForOneFile_DownloadsItOnce_AndSaysSoWhileItRuns()
    {
        var release = new TaskCompletionSource();
        var requests = 0;
        var (downloader, _) = Make(async _ =>
        {
            Interlocked.Increment(ref requests);
            await release.Task;
            return File(16);
        });
        var changes = 0;
        downloader.Changed += () => Interlocked.Increment(ref changes);
        var request = new ObservationDownloadRequest("ivo://cadc/M31", PathFor("m31.fits"), Url: Url);

        var first = downloader.Start(request);
        var second = downloader.Start(request);

        Assert.Same(first, second);
        Assert.True(downloader.IsDownloading("ivo://cadc/M31"));

        release.SetResult();
        await first;
        await WaitUntil(() => !downloader.IsDownloading("ivo://cadc/M31") && Volatile.Read(ref changes) >= 2);

        Assert.Equal(1, requests);
        Assert.Equal(2, changes); // started, and finished: a screen hears both
    }

    /// <summary>The bookkeeping after the work finishes runs on its own continuation; give it a moment.</summary>
    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(20);
        Assert.True(condition());
    }
}
