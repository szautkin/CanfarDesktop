using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Models.ImageDiscovery;

namespace CanfarDesktop.Services.ImageDiscovery;

/// <summary>
/// Orchestrates per-image discovery: upload the probe/inspector script once → launch the probe job
/// (with the Skaha-race retry) → poll until terminal → fetch the manifest from VOSpace → parse →
/// store. Concurrent callers for the same image coalesce onto one in-flight task. Failures are
/// cached as typed outcomes. Timeouts/backoffs are injectable so the logic is unit-testable with
/// mocked facades.
/// </summary>
public class ImageDiscoveryCoordinator
{
    private readonly IManifestStore _store;
    private readonly IHeadlessProbeLauncher _headless;
    private readonly IVoSpaceFileTransfer _vospace;
    private readonly IProbeScriptProvider _scripts;
    private readonly Func<string> _usernameProvider;

    private readonly Func<string, Task<IReadOnlyList<string>?>> _imageTypesLookup;
    private readonly Func<Task<string?>> _registryAuthProvider;
    private readonly Func<Task<string>> _inspectorImageResolver;
    private readonly Func<int, Task> _raceDelay;
    private readonly Func<Task> _pollDelay;
    private readonly int _maxPolls;

    private readonly object _inFlightGate = new();
    private readonly Dictionary<string, Task<ImageManifest>> _inFlight = new();
    private readonly SemaphoreSlim _scriptGate = new(1, 1);
    private bool _probeUploaded;
    private bool _inspectorUploaded;

    public ImageDiscoveryCoordinator(
        IManifestStore store,
        IHeadlessProbeLauncher headless,
        IVoSpaceFileTransfer vospace,
        IProbeScriptProvider scripts,
        Func<string> usernameProvider,
        Func<string, Task<IReadOnlyList<string>?>>? imageTypesLookup = null,
        Func<Task<string?>>? registryAuthProvider = null,
        Func<Task<string>>? inspectorImageResolver = null,
        Func<int, Task>? raceDelay = null,
        Func<Task>? pollDelay = null,
        int maxPolls = 200)
    {
        _store = store;
        _headless = headless;
        _vospace = vospace;
        _scripts = scripts;
        _usernameProvider = usernameProvider;
        _imageTypesLookup = imageTypesLookup ?? (_ => Task.FromResult<IReadOnlyList<string>?>(null));
        _registryAuthProvider = registryAuthProvider ?? (() => Task.FromResult<string?>(null));
        _inspectorImageResolver = inspectorImageResolver ?? (() => Task.FromResult("images.canfar.net/skaha/terminal:1.1.2"));
        _raceDelay = raceDelay ?? (s => Task.Delay(TimeSpan.FromSeconds(s)));
        _pollDelay = pollDelay ?? (() => Task.Delay(TimeSpan.FromSeconds(3)));
        _maxPolls = maxPolls;
    }

    // Cache pass-throughs.
    public LastOutcome? Outcome(string imageID) => _store.Outcome(imageID);
    public IReadOnlyList<string> Search(PackageQuery query) => _store.Search(query);
    public IReadOnlyList<PartialMatch> SearchPartial(PackageQuery query, double minScore, int limit) => _store.SearchPartial(query, minScore, limit);
    public IReadOnlyList<string> KnownImages() => _store.KnownImages();
    public AllPackages AllPackages() => _store.AllPackages();
    public void ClearCache() => _store.Clear();
    public void Invalidate(string imageID) => _store.Invalidate(imageID);
    public int CacheCount() => _store.Count();
    public Task<string> FetchLogsAsync(string jobId, CancellationToken ct = default) => _headless.GetLogsAsync(jobId, ct);
    public Task<string> FetchEventsAsync(string jobId, CancellationToken ct = default) => _headless.GetEventsAsync(jobId, ct);

    public int InFlightCount()
    {
        lock (_inFlightGate) return _inFlight.Count;
    }

    /// <summary>Discover packages for one image (cache hit short-circuits unless <paramref name="force"/>).</summary>
    public async Task<ImageManifest> DiscoverAsync(string imageID, bool force = false, CancellationToken cancellationToken = default)
    {
        if (!force && _store.Outcome(imageID) is { IsSuccess: true, Manifest: { } cached })
            return cached;

        Task<ImageManifest> task;
        lock (_inFlightGate)
        {
            if (!_inFlight.TryGetValue(imageID, out task!))
            {
                task = RunDiscoveryAsync(imageID, force, cancellationToken);
                _inFlight[imageID] = task;
            }
        }

        try
        {
            return await task;
        }
        finally
        {
            lock (_inFlightGate)
            {
                if (_inFlight.TryGetValue(imageID, out var current) && ReferenceEquals(current, task))
                    _inFlight.Remove(imageID);
            }
        }
    }

    public async Task<ImageManifest> RediscoverAsync(string imageID, CancellationToken cancellationToken = default)
    {
        _store.Invalidate(imageID);
        return await DiscoverAsync(imageID, force: true, cancellationToken);
    }

    /// <summary>Re-check VOSpace for a manifest that landed late, without launching a new probe.</summary>
    public async Task<ImageManifest?> RecoverFromVoSpaceAsync(string imageID, CancellationToken cancellationToken = default)
    {
        var manifest = await FetchManifestIfPresentAsync(imageID, cancellationToken);
        if (manifest is not null) _store.SetManifest(manifest);
        return manifest;
    }

    private async Task<ImageManifest> RunDiscoveryAsync(string imageID, bool force, CancellationToken ct)
    {
        var strategy = DiscoveryHeuristics.Strategy(await _imageTypesLookup(imageID));
        CrashLogger.Info($"[Discovery] START image={imageID} force={force} strategy={strategy} user={_usernameProvider()}");

        // Every VOSpace path is /arc/.../home/{username}/...; an empty username collapses the path
        // (home//.verbinal → 404, home/.verbinal → 403) and fails opaquely. Fail clearly instead.
        if (string.IsNullOrWhiteSpace(_usernameProvider()))
        {
            CrashLogger.Info($"[Discovery] ABORT image={imageID}: no signed-in username");
            var e = ImageDiscoveryException.JobSubmitFailed("Not signed in — sign in to CANFAR before inspecting images.");
            PersistFailure(imageID, e, null);
            throw e;
        }

        try
        {
            await EnsureScriptAsync(strategy, ct);
        }
        catch (Exception ex)
        {
            CrashLogger.Info($"[Discovery] script upload FAILED image={imageID}: {ex.Message}");
            var e = ImageDiscoveryException.JobSubmitFailed($"script upload: {ex.Message}");
            PersistFailure(imageID, e, null);
            throw e;
        }

        // Recovery short-circuit: a previous probe may have written the manifest already.
        if (!force)
        {
            var recovered = await FetchManifestIfPresentAsync(imageID, ct);
            CrashLogger.Info($"[Discovery] pre-launch manifest recovery image={imageID} -> {(recovered is null ? "none (will launch job)" : "FOUND, short-circuit")}");
            if (recovered is not null) { _store.SetManifest(recovered); return recovered; }
        }

        string jobId;
        try
        {
            jobId = await LaunchWithRetryAsync(strategy, imageID, ct);
            CrashLogger.Info($"[Discovery] job launched image={imageID} jobId={jobId}");
        }
        catch (ImageDiscoveryException ide) { CrashLogger.Info($"[Discovery] launch FAILED image={imageID}: {ide.Message}"); PersistFailure(imageID, ide, null); throw; }
        catch (HeadlessLaunchException hle) { CrashLogger.Info($"[Discovery] launch FAILED image={imageID}: {hle.Message}"); var e = ImageDiscoveryException.JobSubmitFailed(hle.Message); PersistFailure(imageID, e, null); throw e; }
        catch (Exception ex) { CrashLogger.Info($"[Discovery] launch FAILED image={imageID}: {ex.Message}"); var e = ImageDiscoveryException.JobSubmitFailed(ex.Message); PersistFailure(imageID, e, null); throw e; }

        try
        {
            await PollUntilTerminalAsync(jobId, ct);
            CrashLogger.Info($"[Discovery] job terminal (ok) image={imageID} jobId={jobId}");
        }
        catch (Exception ex)
        {
            CrashLogger.Info($"[Discovery] poll/job FAILED image={imageID} jobId={jobId}: {ex.Message} — trying late manifest fetch");
            // The job may have written the manifest just after our last poll — try a fetch first.
            var recovered = await FetchManifestIfPresentAsync(imageID, ct);
            if (recovered is not null) { _store.SetManifest(recovered); return recovered; }
            await CollectJobDiagnosticsAsync(jobId, ct); // logs events/logs to crash.log so the failure is explainable
            var e = ex as ImageDiscoveryException ?? ImageDiscoveryException.Unknown(ex.Message);
            PersistFailure(imageID, e, jobId);
            throw e;
        }

        string json;
        try
        {
            json = await FetchManifestDataAsync(imageID, ct);
        }
        catch (Exception ex)
        {
            CrashLogger.Info($"[Discovery] manifest fetch FAILED image={imageID} jobId={jobId}: {ex.Message} — job ran but wrote no manifest; collecting job diagnostics");
            var diag = await CollectJobDiagnosticsAsync(jobId, ct);
            var e = ImageDiscoveryException.ManifestFetchFailed($"{ex.Message} — {diag}");
            PersistFailure(imageID, e, jobId);
            throw e;
        }

        ImageManifest manifest;
        try
        {
            manifest = ManifestParser.Parse(json);
        }
        catch (ManifestParseException pe) { CrashLogger.Info($"[Discovery] manifest PARSE FAILED image={imageID}: {pe.Message}"); var e = ImageDiscoveryException.ManifestParseFailed(pe.Message); PersistFailure(imageID, e, jobId); throw e; }

        var pkgCount = manifest.DpkgPackages.Count + manifest.RpmPackages.Count + manifest.ApkPackages.Count
            + manifest.PythonPackages.Count + manifest.RPackages.Count;
        CrashLogger.Info($"[Discovery] SUCCESS image={imageID} packages={pkgCount}");
        _store.SetManifest(manifest);
        return manifest;
    }

    private async Task EnsureScriptAsync(ProbeStrategy strategy, CancellationToken ct)
    {
        await _scriptGate.WaitAsync(ct);
        try
        {
            var inTarget = strategy == ProbeStrategy.InTarget;
            if (inTarget ? _probeUploaded : _inspectorUploaded) return;

            var body = inTarget ? _scripts.ProbeBody : _scripts.InspectorBody;
            var fileName = inTarget ? _scripts.ProbeUploadFileName : _scripts.InspectorUploadFileName;

            var username = _usernameProvider();
            await _vospace.EnsureFolderAsync(username, string.Empty, _scripts.HomeSubdirectory, ct);

            var tempPath = Path.Combine(Path.GetTempPath(), $"verbinal-{fileName}");
            await File.WriteAllTextAsync(tempPath, body, ct);
            CrashLogger.Info($"[Discovery] uploading script -> {username}/{_scripts.HomeSubdirectory}/{fileName} (strategy={strategy})");
            try
            {
                await _vospace.UploadFileAsync(username, $"{_scripts.HomeSubdirectory}/{fileName}", tempPath, ct);
            }
            finally
            {
                try { File.Delete(tempPath); } catch { /* best effort */ }
            }

            if (inTarget) _probeUploaded = true; else _inspectorUploaded = true;
        }
        finally
        {
            _scriptGate.Release();
        }
    }

    private Task<string> LaunchWithRetryAsync(ProbeStrategy strategy, string imageID, CancellationToken ct)
        => RetryingOnRaceAsync(() => strategy == ProbeStrategy.InTarget
            ? LaunchProbeJobAsync(imageID, ct)
            : LaunchInspectorJobAsync(imageID, ct));

    private async Task<string> RetryingOnRaceAsync(Func<Task<string>> work)
    {
        foreach (var delaySeconds in DiscoveryHeuristics.SkahaRaceBackoffsSeconds)
        {
            try { return await work(); }
            catch (Exception ex) when (DiscoveryHeuristics.IsSkahaJobNotFoundRace(ex.Message))
            {
                await _raceDelay(delaySeconds);
            }
        }
        try
        {
            return await work();
        }
        catch (Exception ex) when (DiscoveryHeuristics.IsSkahaJobNotFoundRace(ex.Message))
        {
            throw ImageDiscoveryException.JobSubmitFailed(
                $"Skaha couldn't see the job it just created after {DiscoveryHeuristics.SkahaRaceBackoffsSeconds.Length + 1} attempts (informer-cache lag, not a quota issue): {ex.Message}");
        }
    }

    private async Task<string> LaunchProbeJobAsync(string imageID, CancellationToken ct)
    {
        var name = DiscoveryHeuristics.MakeJobName("vp", imageID, DiscoveryHeuristics.NewJobSuffix());
        var scriptPath = $"/arc/home/{_usernameProvider()}/{_scripts.HomeSubdirectory}/{_scripts.ProbeUploadFileName}";
        CrashLogger.Info($"[Discovery] PROBE job name={name} image={imageID} scriptPath={scriptPath}");
        var p = new SessionLaunchParams
        {
            Image = imageID, Name = name, Cmd = "bash", Args = scriptPath,
            Cores = 1, Ram = 1, Gpus = 0, Replicas = 1,
            Env = { new("IMAGE_ID", imageID) },
            RegistryAuthHeader = await _registryAuthProvider(),
        };
        var ids = await _headless.LaunchHeadlessAsync(p, ct);
        return ids.FirstOrDefault() ?? throw ImageDiscoveryException.JobSubmitFailed("Skaha returned no job id");
    }

    private async Task<string> LaunchInspectorJobAsync(string targetImageID, CancellationToken ct)
    {
        var name = DiscoveryHeuristics.MakeJobName("vi", targetImageID, DiscoveryHeuristics.NewJobSuffix());
        var scriptPath = $"/arc/home/{_usernameProvider()}/{_scripts.HomeSubdirectory}/{_scripts.InspectorUploadFileName}";
        var inspectorImage = await _inspectorImageResolver();
        CrashLogger.Info($"[Discovery] INSPECTOR job name={name} inspectorImage={inspectorImage} scriptPath={scriptPath} target={targetImageID}");
        var p = new SessionLaunchParams
        {
            Image = inspectorImage, Name = name, Cmd = "bash", Args = scriptPath,
            Cores = 1, Ram = 1, Gpus = 0, Replicas = 1,
            Env = { new("TARGET_IMAGE", targetImageID) },
            RegistryAuthHeader = await _registryAuthProvider(),
        };
        var ids = await _headless.LaunchHeadlessAsync(p, ct);
        return ids.FirstOrDefault() ?? throw ImageDiscoveryException.JobSubmitFailed("Skaha returned no job id");
    }

    private async Task PollUntilTerminalAsync(string jobId, CancellationToken ct)
    {
        string? lastStatus = null;
        for (var i = 0; i < _maxPolls; i++)
        {
            IReadOnlyList<HeadlessJobStatus> jobs;
            try { jobs = await _headless.GetHeadlessJobsAsync(ct); }
            catch (Exception ex) { throw ImageDiscoveryException.Unknown($"poll: {ex.Message}"); }

            var job = jobs.FirstOrDefault(j => j.Id == jobId);
            if (job is null)
            {
                CrashLogger.Info($"[Discovery] poll jobId={jobId} no longer in listing after {i} polls (reaped/completed) — will validate via manifest fetch");
                return; // dropped from listing → reaped after completion; manifest fetch validates
            }
            if (job.Status != lastStatus)
            {
                CrashLogger.Info($"[Discovery] poll jobId={jobId} status={job.Status} (poll {i})");
                lastStatus = job.Status;
            }
            if (job.IsTerminal)
            {
                if (job.IsFailed) throw ImageDiscoveryException.Unknown($"job ended in failed state: {job.Status}");
                return;
            }
            await _pollDelay();
        }
        throw ImageDiscoveryException.JobTimedOut();
    }

    /// <summary>
    /// Best-effort fetch of a failed job's Skaha events + logs (the "why didn't it write a manifest"
    /// answer: ImagePullBackOff, registry-auth, syft failure, OOM…). Logs the full tail to crash.log
    /// and returns a short events tail to fold into the user-facing exception message. Never throws.
    /// </summary>
    private async Task<string> CollectJobDiagnosticsAsync(string jobId, CancellationToken ct)
    {
        string events, logs;
        try { events = await _headless.GetEventsAsync(jobId, ct); }
        catch (Exception ex) { events = $"(events unavailable: {ex.Message})"; }
        try { logs = await _headless.GetLogsAsync(jobId, ct); }
        catch (Exception ex) { logs = $"(logs unavailable: {ex.Message})"; }

        CrashLogger.Info($"[Discovery] job {jobId} EVENTS:\n{Tail(events, 2500)}");
        CrashLogger.Info($"[Discovery] job {jobId} LOGS:\n{Tail(logs, 2500)}");
        return $"job {jobId} events: {Tail(events, 500)}";
    }

    private static string Tail(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return "(empty)";
        s = s.Trim();
        return s.Length <= max ? s : "…" + s[^max..];
    }

    private async Task<string> FetchManifestDataAsync(string imageID, CancellationToken ct)
    {
        var path = $"{_scripts.HomeSubdirectory}/manifests/{ImageManifest.Sanitize(imageID)}.json";
        CrashLogger.Info($"[Discovery] fetch manifest path={_usernameProvider()}/{path}");
        var temp = await _vospace.DownloadToTempAsync(_usernameProvider(), path, ct);
        try { return await File.ReadAllTextAsync(temp, ct); }
        finally { try { File.Delete(temp); } catch { /* best effort */ } }
    }

    /// <summary>Read+parse the manifest from VOSpace; null on missing/unparseable/mismatched/stub.</summary>
    private async Task<ImageManifest?> FetchManifestIfPresentAsync(string imageID, CancellationToken ct)
    {
        string json;
        try { json = await FetchManifestDataAsync(imageID, ct); }
        catch { return null; }

        ImageManifest manifest;
        try { manifest = ManifestParser.Parse(json); }
        catch { return null; }

        if (manifest.ImageID != imageID) return null;          // wrote-for-another-image guard
        if (DiscoveryHeuristics.IsStubManifest(manifest)) return null; // refuse to cache failure placeholders
        return manifest;
    }

    private void PersistFailure(string imageID, ImageDiscoveryException error, string? jobId)
    {
        try { _store.SetFailure(imageID, error.Category, error.Message, DateTimeOffset.UtcNow, jobId); }
        catch { /* never let failure-persistence mask the real error */ }
    }
}
