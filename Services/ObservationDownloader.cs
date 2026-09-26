using CanfarDesktop.Helpers;
using CanfarDesktop.Models;

namespace CanfarDesktop.Services;

/// <summary>What to download, where to, and what it becomes in Research.</summary>
/// <param name="Record">
/// The Research record as it should stand once the file is there, built BEFORE the download from what
/// the caller knows then — so finishing needs nothing from the screen that asked. Its local path and
/// size are filled in on arrival. Null for a plain save that Research does not track.
/// </param>
/// <param name="Url">The file's address, when the caller has already chosen one (Search's artifact
/// picker). Otherwise it is resolved from the publisher id and <paramref name="ArtifactIndex"/>.</param>
public sealed record ObservationDownloadRequest(
    string PublisherId,
    string TargetPath,
    DownloadedObservation? Record = null,
    string? Url = null,
    int? ArtifactIndex = null);

/// <summary>
/// Observation downloads, owned by the app rather than by the screen that asked for one.
///
/// <para>There were three, and each lived and died with its caller. Research's re-read the SELECTED
/// observation when the bytes arrived, so closing the detail mid-download threw — the file landed and
/// the record never heard — and choosing another observation wrote the path onto THAT one. Search's
/// reported progress only to its own banner, and the agent's only to the agent. None of them told the
/// status bar, which exists so that work outlives the control that started it.</para>
///
/// <para>So the work is started here and runs here, whatever happens to the screen: progress goes to
/// the status bar (<see cref="TaskRegistry"/>), the outcome is recorded against the observation's
/// publisher id, and <see cref="ObservationStore.Changed"/> tells any screen still showing it.</para>
/// </summary>
public sealed class ObservationDownloader
{
    /// <summary>A transfer silent this long is dead; <see cref="StreamToFile"/> gives up on it.</summary>
    public static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);

    /// <summary>How often the status bar hears about progress: often enough to move, rarely enough to read.</summary>
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(250);

    private readonly Func<ObservationDownloadService> _downloads;
    private readonly ObservationStore _store;
    private readonly object _gate = new();
    private readonly Dictionary<string, (string PublisherId, string? ProductKey, Task Work)> _running =
        new(StringComparer.OrdinalIgnoreCase);

    /// <param name="downloads">A factory rather than an instance: the download service sits on a typed
    /// HTTP client, and one held for the app's life would never see its handler renewed.</param>
    public ObservationDownloader(Func<ObservationDownloadService> downloads, ObservationStore store)
    {
        _downloads = downloads;
        _store = store;
    }

    /// <summary>
    /// Raised when a download starts or ends, however it ends, on whatever thread that happened. A
    /// failure changes nothing in the store, so without this a screen showing "downloading" would show
    /// it for good.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Whether this product of an observation is downloading right now — the complete one when
    /// <paramref name="productKey"/> is null, one cutout otherwise — for a screen deciding what to offer.
    /// A cutout on its way says nothing about the full file, and the other way round.
    /// </summary>
    public bool IsDownloading(string publisherId, string? productKey = null)
    {
        lock (_gate)
            return _running.Values.Any(r => r.PublisherId == publisherId && r.ProductKey == productKey && !r.Work.IsCompleted);
    }

    /// <summary>
    /// Start it and return at once. The returned task is for a caller that wants to wait — an agent's
    /// apply does; a screen need not, and a failure it does not watch is still reported in the status bar.
    ///
    /// <para>Asking again for a file already on its way returns that download rather than starting a
    /// second: two writers of one file would each tear down the other's temporary copy.</para>
    /// </summary>
    public Task Start(ObservationDownloadRequest request)
    {
        Task work;
        lock (_gate)
        {
            if (_running.TryGetValue(request.TargetPath, out var already) && !already.Work.IsCompleted)
                return already.Work;

            work = Task.Run(() => RunAsync(request));
            _running[request.TargetPath] = (request.PublisherId, request.Record?.ProductKey, work);
        }

        RaiseChanged();
        _ = work.ContinueWith(done =>
        {
            _ = done.Exception; // observed: the status bar already has the reason
            lock (_gate)
            {
                if (_running.TryGetValue(request.TargetPath, out var entry) && entry.Work == done)
                    _running.Remove(request.TargetPath);
            }
            RaiseChanged();
        }, TaskScheduler.Default);
        return work;
    }

    private void RaiseChanged()
    {
        try { Changed?.Invoke(); }
        catch { /* a broken subscriber must not take the download down with it */ }
    }

    private async Task RunAsync(ObservationDownloadRequest request)
    {
        using var task = TaskRegistry.Begin(TaskKind.Download, $"Download {Path.GetFileName(request.TargetPath)}");
        try
        {
            var downloads = _downloads();
            var url = request.Url;
            if (url is null)
            {
                task.Stage("finding the file");
                url = await downloads.ResolveUrlAsync(request.PublisherId, request.ArtifactIndex);
            }

            task.Stage("connecting");
            await downloads.DownloadToPathAsync(url, request.TargetPath,
                progress: new StageProgress(task), stallTimeout: StallTimeout);

            long? size = new FileInfo(request.TargetPath) is { Exists: true } file ? file.Length : null;
            if (request.Record is { } record)
            {
                record.LocalPath = request.TargetPath;
                record.FileSize = size;
                _store.Save(record);
            }

            task.Succeed(Caom2Format.Bytes(size));
        }
        catch (Exception ex)
        {
            task.Fail(ex.Message);
            throw;
        }
    }

    /// <summary>What the status bar says while the bytes arrive.</summary>
    public static string Describe(long downloaded, long? total)
        => total is long t && t > 0
            ? $"{Caom2Format.Bytes(downloaded)} of {Caom2Format.Bytes(t)} · {(double)downloaded / t:P0}"
            : $"{Caom2Format.Bytes(downloaded)} so far"; // CADC sends no length for a package it builds on the fly

    /// <summary>Bytes so far as the task's stage, at most every <see cref="ProgressInterval"/>; the last always gets through.</summary>
    private sealed class StageProgress(TaskHandle task) : IProgress<(long Downloaded, long? Total)>
    {
        private long? _last;

        public void Report((long Downloaded, long? Total) value)
        {
            var now = Environment.TickCount64;
            var done = value.Total is long t && value.Downloaded >= t;
            if (!done && _last is long last && now - last < ProgressInterval.TotalMilliseconds) return;

            _last = now;
            task.Stage(Describe(value.Downloaded, value.Total));
        }
    }
}
