using System.Text.Json;
using Xunit;
using CanfarDesktop.Models;
using CanfarDesktop.Models.ImageDiscovery;
using CanfarDesktop.Services.ImageDiscovery;

namespace CanfarDesktop.Tests.Services;

public class ImageDiscoveryCoordinatorTests
{
    // ── Fakes ───────────────────────────────────────────────────────────────

    private sealed class FakeStore : IManifestStore
    {
        public readonly Dictionary<string, LastOutcome> Items = new();
        public LastOutcome? Outcome(string id) => Items.GetValueOrDefault(id);
        public void SetManifest(ImageManifest m) => Items[m.ImageID] = LastOutcome.Success(m);
        public void SetFailure(string id, FailureCategory c, string msg, DateTimeOffset at, string? job) => Items[id] = LastOutcome.Failure(id, c, msg, at, job);
        public void Invalidate(string id) => Items.Remove(id);
        public void Clear() => Items.Clear();
        public IReadOnlyList<string> Search(PackageQuery q) => Items.Where(kv => kv.Value.IsSuccess).Select(kv => kv.Key).ToList();
        public IReadOnlyList<PartialMatch> SearchPartial(PackageQuery q, double m, int l) => Array.Empty<PartialMatch>();
        public IReadOnlyList<string> KnownImages() => Items.Keys.ToList();
        public AllPackages AllPackages() => new();
        public int Count() => Items.Count;
    }

    private sealed class FakeHeadless : IHeadlessProbeLauncher
    {
        public int LaunchCount;
        public Func<int, Exception?>? LaunchError; // attempt# → error (null = success)
        public Action? OnLaunch;
        public Task? LaunchGate;
        public bool JobTerminal = true;
        public bool JobFailed;

        public async Task<IReadOnlyList<string>> LaunchHeadlessAsync(SessionLaunchParams p, CancellationToken ct)
        {
            LaunchCount++;
            if (LaunchGate is not null) await LaunchGate;
            if (LaunchError?.Invoke(LaunchCount) is { } err) throw err;
            OnLaunch?.Invoke();
            return new[] { $"job-{LaunchCount}" };
        }

        public Task<IReadOnlyList<HeadlessJobStatus>> GetHeadlessJobsAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<HeadlessJobStatus>>(
                new[] { new HeadlessJobStatus($"job-{LaunchCount}", JobFailed ? "Failed" : "Succeeded", JobTerminal, JobFailed) });

        public Task<string> GetLogsAsync(string id, CancellationToken ct) => Task.FromResult("logs");
        public Task<string> GetEventsAsync(string id, CancellationToken ct) => Task.FromResult("events");
    }

    private sealed class FakeVoSpace : IVoSpaceFileTransfer
    {
        public string? ManifestJson;
        public int Uploads;

        public Task UploadFileAsync(string username, string remotePath, string localPath, CancellationToken ct) { Uploads++; return Task.CompletedTask; }
        public Task EnsureFolderAsync(string username, string parentPath, string folderName, CancellationToken ct) => Task.CompletedTask;

        public async Task<string> DownloadToTempAsync(string username, string path, CancellationToken ct)
        {
            if (ManifestJson is null) throw new FileNotFoundException(path);
            var tmp = Path.GetTempFileName();
            await File.WriteAllTextAsync(tmp, ManifestJson, ct);
            return tmp;
        }
    }

    private sealed class FakeScripts : IProbeScriptProvider
    {
        public string HomeSubdirectory => ".verbinal";
        public string ProbeBody => "#probe";
        public string InspectorBody => "#inspector";
        public string ProbeUploadFileName => "probe-abc123.sh";
        public string InspectorUploadFileName => "inspector-abc123.sh";
    }

    private static string ManifestJson(string imageID) => JsonSerializer.Serialize(new ImageManifest
    {
        ImageID = imageID,
        OsFamily = "ubuntu",
        CapturedAt = DateTimeOffset.UtcNow,
        ApkPackages = new[] { new ImagePackage("musl", "1") }, // non-empty → not a stub
    });

    private static ImageDiscoveryCoordinator Make(FakeStore store, FakeHeadless hl, FakeVoSpace vs)
        => new(store, hl, vs, new FakeScripts(), () => "alice",
            imageTypesLookup: _ => Task.FromResult<IReadOnlyList<string>?>(new[] { "headless" }),
            raceDelay: _ => Task.CompletedTask,
            pollDelay: () => Task.CompletedTask,
            maxPolls: 5);

    // ── Tests ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task CacheHit_ReturnsCached_NoLaunch()
    {
        var store = new FakeStore();
        store.SetManifest(new ImageManifest { ImageID = "img:1", ApkPackages = new[] { new ImagePackage("musl", "1") } });
        var hl = new FakeHeadless();

        var m = await Make(store, hl, new FakeVoSpace()).DiscoverAsync("img:1");

        Assert.Equal("img:1", m.ImageID);
        Assert.Equal(0, hl.LaunchCount);
    }

    [Fact]
    public async Task SuccessViaLaunch_PollFetchParseStore()
    {
        var store = new FakeStore();
        var vs = new FakeVoSpace();
        var hl = new FakeHeadless { OnLaunch = () => vs.ManifestJson = ManifestJson("img:2") };

        var m = await Make(store, hl, vs).DiscoverAsync("img:2");

        Assert.Equal("img:2", m.ImageID);
        Assert.Equal(1, hl.LaunchCount);
        Assert.True(vs.Uploads >= 1);                 // script uploaded once
        Assert.True(store.Outcome("img:2")!.IsSuccess);
    }

    [Fact]
    public async Task RetriesOnSkahaRace_ThenSucceeds()
    {
        var store = new FakeStore();
        var vs = new FakeVoSpace();
        var hl = new FakeHeadless
        {
            LaunchError = attempt => attempt <= 2 ? new HttpRequestException("HTTP 500: jobs.batch \"x\" not found") : null,
            OnLaunch = () => vs.ManifestJson = ManifestJson("img:3"),
        };

        var m = await Make(store, hl, vs).DiscoverAsync("img:3");

        Assert.Equal(3, hl.LaunchCount); // 2 race failures + 1 success
        Assert.Equal("img:3", m.ImageID);
    }

    [Fact]
    public async Task SubmitFailure_IsTypedAndCachedAsFailure()
    {
        var store = new FakeStore();
        var hl = new FakeHeadless { LaunchError = _ => new HttpRequestException("HTTP 400 private image") };

        var ex = await Assert.ThrowsAsync<ImageDiscoveryException>(() => Make(store, hl, new FakeVoSpace()).DiscoverAsync("img:4"));

        Assert.Equal(FailureCategory.JobSubmitFailed, ex.Category);
        Assert.False(store.Outcome("img:4")!.IsSuccess);
        Assert.Equal(FailureCategory.JobSubmitFailed, store.Outcome("img:4")!.Category);
    }

    [Fact]
    public async Task JobTimeout_IsTypedAndCached()
    {
        var store = new FakeStore();
        var hl = new FakeHeadless { JobTerminal = false }; // never terminal → poll exhausts

        var ex = await Assert.ThrowsAsync<ImageDiscoveryException>(() => Make(store, hl, new FakeVoSpace()).DiscoverAsync("img:5"));

        Assert.Equal(FailureCategory.JobTimedOut, ex.Category);
        Assert.Equal(FailureCategory.JobTimedOut, store.Outcome("img:5")!.Category);
    }

    [Fact]
    public async Task JobEndsInFailedState_IsTypedAndCached()
    {
        var store = new FakeStore();
        var hl = new FakeHeadless { JobFailed = true }; // terminal + failed

        var ex = await Assert.ThrowsAsync<ImageDiscoveryException>(() => Make(store, hl, new FakeVoSpace()).DiscoverAsync("img:7"));

        Assert.Equal(FailureCategory.Unknown, ex.Category); // "job ended in failed state"
        Assert.False(store.Outcome("img:7")!.IsSuccess);
    }

    [Fact]
    public async Task EmptyUsername_AbortsBeforeAnyVoSpaceOrLaunch()
    {
        // Regression: AuthService is a transient typed-HttpClient, so its CurrentUsername was lost
        // between resolutions → the coordinator saw "" and built home//.verbinal paths that 404/403'd
        // opaquely. The guard must fail fast and clearly without touching VOSpace or Skaha.
        var store = new FakeStore();
        var hl = new FakeHeadless();
        var vs = new FakeVoSpace();
        var c = new ImageDiscoveryCoordinator(store, hl, vs, new FakeScripts(), usernameProvider: () => "  ",
            imageTypesLookup: _ => Task.FromResult<IReadOnlyList<string>?>(null), // inspector path
            raceDelay: _ => Task.CompletedTask, pollDelay: () => Task.CompletedTask, maxPolls: 5);

        var ex = await Assert.ThrowsAsync<ImageDiscoveryException>(() => c.DiscoverAsync("img:noauth"));

        Assert.Equal(FailureCategory.JobSubmitFailed, ex.Category);
        Assert.Contains("signed in", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, hl.LaunchCount);   // never launched a job
        Assert.Equal(0, vs.Uploads);       // never uploaded a script
        Assert.Equal(FailureCategory.JobSubmitFailed, store.Outcome("img:noauth")!.Category);
    }

    [Fact]
    public async Task ConcurrentDiscover_Coalesces_OneLaunch()
    {
        var store = new FakeStore();
        var vs = new FakeVoSpace();
        var gate = new TaskCompletionSource();
        var hl = new FakeHeadless { LaunchGate = gate.Task, OnLaunch = () => vs.ManifestJson = ManifestJson("img:6") };
        var c = Make(store, hl, vs);

        var t1 = c.DiscoverAsync("img:6");
        var t2 = c.DiscoverAsync("img:6");
        gate.SetResult();
        await Task.WhenAll(t1, t2);

        Assert.Equal(1, hl.LaunchCount); // both callers shared one probe
    }
}
