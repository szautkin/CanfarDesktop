using System.Net;
using Xunit;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Models.Cutouts;
using CanfarDesktop.Services;
using CanfarDesktop.Services.Cutouts;
using CanfarDesktop.Services.Cutouts.Local;
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

    /// <summary>
    /// A cutout record fetched again — its file removed, or never there — comes back as the cutout it
    /// is: its file's SODA service looked up from DataLink, and the request built from its own region.
    /// Never the whole file it was cut from.
    /// </summary>
    [Fact]
    public async Task ACutoutRecord_IsFetchedAsItsCutout_NotAsTheWholeFile()
    {
        var requested = new List<string>();
        var (downloader, store) = Make(req =>
        {
            var uri = req.RequestUri!.ToString();
            requested.Add(uri);
            return Task.FromResult(uri.Contains("datalink", StringComparison.OrdinalIgnoreCase)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Cutouts.SodaDescriptorParserTests.Fixture("megapipe-image.xml")) }
                : File(64));
        });
        var record = new DownloadedObservation
        {
            PublisherID = "ivo://cadc.nrc.ca/CFHTMEGAPIPE?G006.010.684+41.269/G006.010.684+41.269.R",
            Cutout = new CanfarDesktop.Models.Cutouts.CutoutSpec
            {
                ArtifactId = "cadc:CFHTSG/G006.010.684+41.269.R.fits",
                Region = CanfarDesktop.Models.Cutouts.SkyRegion.Circle(10.68, 41.27, 0.05),
            },
        };

        await downloader.Start(new ObservationDownloadRequest(record.PublisherID, PathFor("cut.fits"), record));

        Assert.Contains(requested, u => u.StartsWith("https://ws.cadc-ccda.hia-iha.nrc-cnrc.gc.ca/caom2ops/sync?ID=")
                                        && u.Contains("CIRCLE=10.68"));
        Assert.True(Assert.Single(store.Observations).IsCutout);
    }

    /// <summary>
    /// A cutout is made by whoever its method says cuts it, through the same downloader: the maker only
    /// produces the file; the record, its path and size, and the status bar are the downloader's as for
    /// any download.
    /// </summary>
    [Fact]
    public async Task ACutout_IsMadeByTheMakerForItsMethod_AndRecordedLikeAnyDownload()
    {
        var store = new ObservationStore();
        var maker = new FakeMaker(CutoutMethod.Local, bytes: 300);
        var downloader = new ObservationDownloader(() => throw new InvalidOperationException("no network for a local cut"),
            store, [maker]);
        var record = new DownloadedObservation
        {
            PublisherID = "ivo://cadc/HST",
            Cutout = new CutoutSpec { ArtifactId = "cadc:HST/x_flt.fits", Region = SkyRegion.Circle(1, 2, 0.01), CutBy = CutoutMethod.Local },
        };

        await downloader.Start(new ObservationDownloadRequest(record.PublisherID, PathFor("cut.fits"), record));

        Assert.Equal(record.Cutout, Assert.Single(maker.Jobs).Spec);
        var saved = Assert.Single(store.Observations);
        Assert.Equal(PathFor("cut.fits"), saved.LocalPath);
        Assert.Equal(300, saved.FileSize);
        Assert.Equal("Cut x cut.fits", OnlyTask().Label);
    }

    /// <summary>
    /// Download on a record that says which archive file it holds fetches that file again — not whatever
    /// DataLink ranks first — and one DataLink no longer lists falls back to the observation's file, the
    /// record then no longer claiming to know which it holds.
    /// </summary>
    [Theory]
    [InlineData("cadc:HST/j8pu0y010_flt.fits", "https://archive.test/data/j8pu0y010_flt.fits", "cadc:HST/j8pu0y010_flt.fits")]
    [InlineData("cadc:HST/gone_flt.fits", "https://archive.test/data/j8pu0y010_drz.fits", null)]
    public async Task ARecordThatSaysWhichFile_GetsThatFileAgain(string artifactId, string fetched, string? kept)
    {
        var requested = new List<string>();
        var (downloader, store) = Make(req =>
        {
            var uri = req.RequestUri!.ToString();
            requested.Add(uri);
            return Task.FromResult(uri.Contains("datalink", StringComparison.OrdinalIgnoreCase)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(DataLink(
                    "https://archive.test/data/j8pu0y010_drz.fits", "https://archive.test/data/j8pu0y010_flt.fits")) }
                : File(32));
        });
        var record = new DownloadedObservation { PublisherID = "ivo://cadc.nrc.ca/HST?j8pu0y010/j8pu0y010", ArtifactId = artifactId };

        await downloader.Start(new ObservationDownloadRequest(record.PublisherID, PathFor("x.fits"), record));

        Assert.Contains(fetched, requested);
        Assert.Equal(kept, Assert.Single(store.Observations).ArtifactId);
    }

    /// <summary>A DataLink answer listing these files as #this rows.</summary>
    private static string DataLink(params string[] urls)
    {
        var rows = string.Concat(urls.Select(u =>
            $"<TR><TD>ivo://x</TD><TD>{u}</TD><TD></TD><TD></TD><TD>#this</TD><TD></TD><TD></TD><TD>application/fits</TD><TD>100</TD></TR>"));
        return $$"""
            <?xml version="1.0"?><VOTABLE xmlns="http://www.ivoa.net/xml/VOTable/v1.3"><RESOURCE type="results"><TABLE>
            <FIELD name="ID" datatype="char"/><FIELD name="access_url" datatype="char"/><FIELD name="service_def" datatype="char"/>
            <FIELD name="error_message" datatype="char"/><FIELD name="semantics" datatype="char"/><FIELD name="description" datatype="char"/>
            <FIELD name="content_qualifier" datatype="char"/><FIELD name="content_type" datatype="char"/><FIELD name="content_length" datatype="long"/>
            <DATA><TABLEDATA>{{rows}}</TABLEDATA></DATA></TABLE></RESOURCE></VOTABLE>
            """;
    }

    /// <summary>
    /// The whole way through, as the app runs it: an HST frame downloaded, a local cutout of it made by
    /// the downloader and kept in Research beside it — then its file removed, and Download making it
    /// again from the frame, with no network at all.
    /// </summary>
    [Fact]
    public async Task ALocalCutout_IsCutFromTheDownloadedFile_AndCutAgainOnceItsFileIsRemoved()
    {
        var store = new ObservationStore();
        var cards = SyntheticFits.ImageCards(-32, primary: true, 60, 40);
        cards.AddRange(SyntheticFits.TanWcs(30.5, 20.5, 150.0, 2.2, 1e-4));
        var frame = SyntheticFits.Write(_dir, "j8pu0y010_flt.fits",
            SyntheticFits.Hdu(cards, SyntheticFits.Pixels(-32, 60, 40, (x, y, _) => x + y)));
        store.Save(new DownloadedObservation { PublisherID = "ivo://cadc/HST", LocalPath = frame, ArtifactId = "cadc:HST/j8pu0y010_flt.fits" });
        var downloader = new ObservationDownloader(() => throw new InvalidOperationException("a local cut needs no network"), store,
            [new LocalCutoutMaker((pid, artifact) => LocalCopies.Find(store.Observations, pid, artifact)?.LocalPath)]);

        var local = CutoutSources.Local(store.Observations, "ivo://cadc/HST", ["cadc:HST/j8pu0y010_flt.fits"])!;
        var spec = local.Bind(new CutoutSpec { Region = SkyRegion.Circle(150.0, 2.2, 5e-4) });
        var record = new DownloadedObservation { PublisherID = "ivo://cadc/HST", Cutout = spec };
        await downloader.Start(new ObservationDownloadRequest(record.PublisherID, PathFor("cut.fits"), record));

        Assert.Equal(2, store.Observations.Count); // the frame, and its cutout beside it
        var cutout = Assert.Single(store.Observations, o => o.IsCutout);
        Assert.Equal(CutoutMethod.Local, cutout.Cutout!.CutBy);
        var size = new FileInfo(PathFor("cut.fits")).Length;
        Assert.Equal(local.EstimateBytes(spec), size);

        Assert.Null(ResearchRecords.RemoveLocalFile(store, cutout));
        await downloader.Start(new ObservationDownloadRequest(cutout.PublisherID, PathFor("again.fits"), cutout));

        Assert.Equal(size, new FileInfo(PathFor("again.fits")).Length);
        Assert.Equal(PathFor("again.fits"), Assert.Single(store.Observations, o => o.IsCutout).LocalPath);
    }

    /// <summary>A cutout nobody here can make fails in the status bar with the reason, and records nothing.</summary>
    [Fact]
    public async Task ACutoutWithNoMakerForItsMethod_FailsWithTheReason()
    {
        var (downloader, store) = Make(_ => Task.FromResult(File(16)));
        var record = new DownloadedObservation
        {
            PublisherID = "ivo://cadc/HST",
            Cutout = new CutoutSpec { ArtifactId = "cadc:HST/x_flt.fits", Region = SkyRegion.Circle(1, 2, 0.01), CutBy = CutoutMethod.Local },
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => downloader.Start(new ObservationDownloadRequest(record.PublisherID, PathFor("cut.fits"), record)));

        Assert.Contains("Local", OnlyTask().Message);
        Assert.Empty(store.Observations);
    }

    private sealed class FakeMaker(CutoutMethod method, int bytes) : ICutoutMaker
    {
        public List<CutoutJob> Jobs { get; } = [];
        public CutoutMethod Method => method;
        public string TaskLabel(string fileName) => $"Cut x {fileName}";

        public Task MakeAsync(CutoutJob job, IProgress<string> stage, IProgress<(long Done, long? Total)> progress,
                              CancellationToken ct = default)
        {
            Jobs.Add(job);
            System.IO.File.WriteAllBytes(job.TargetPath, new byte[bytes]);
            progress.Report((bytes, bytes));
            return Task.CompletedTask;
        }
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
