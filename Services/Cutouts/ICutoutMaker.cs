using CanfarDesktop.Models.Cutouts;

namespace CanfarDesktop.Services.Cutouts;

/// <summary>A cutout to make: of which observation, which cutout, and where its file goes.</summary>
public sealed record CutoutJob(string PublisherId, CutoutSpec Spec, string TargetPath);

/// <summary>
/// Makes a cutout's file, one way. The app's downloader runs every cutout the same way — in the status
/// bar, outliving the screen that asked, recorded in Research when it lands — and asks the maker for
/// the cutout's method only for the one step that differs: producing the bytes.
/// </summary>
public interface ICutoutMaker
{
    CutoutMethod Method { get; }

    /// <summary>What the status bar calls the work, for a file of this name.</summary>
    string TaskLabel(string fileName);

    /// <summary>
    /// Write the cutout to <see cref="CutoutJob.TargetPath"/>, telling <paramref name="stage"/> what it
    /// is doing and <paramref name="progress"/> how far it has got. Throws, with the reason, when it
    /// cannot; nothing is left at the target then.
    /// </summary>
    Task MakeAsync(CutoutJob job, IProgress<string> stage, IProgress<(long Done, long? Total)> progress,
                   CancellationToken ct = default);
}

/// <summary>
/// A cutout cut on CADC's side: the SODA request built from the cutout against the file's descriptor as
/// DataLink describes it today — a cutout can be fetched long after it was chosen — then downloaded.
/// </summary>
public sealed class SodaCutoutMaker : ICutoutMaker
{
    private readonly Func<ObservationDownloadService> _downloads;
    private readonly TimeSpan _stallTimeout;

    /// <param name="downloads">A factory, as the downloader's own: the service sits on a typed HTTP client.</param>
    /// <param name="stallTimeout">How long a transfer may go silent before it is given up for dead.</param>
    public SodaCutoutMaker(Func<ObservationDownloadService> downloads, TimeSpan stallTimeout)
    {
        _downloads = downloads;
        _stallTimeout = stallTimeout;
    }

    public CutoutMethod Method => CutoutMethod.Soda;

    public string TaskLabel(string fileName) => $"Download {fileName}";

    public async Task MakeAsync(CutoutJob job, IProgress<string> stage, IProgress<(long Done, long? Total)> progress,
                                CancellationToken ct = default)
    {
        var downloads = _downloads();
        stage.Report("finding the file");
        var url = await downloads.ResolveCutoutUrlAsync(job.PublisherId, job.Spec, ct);

        stage.Report("connecting");
        await downloads.DownloadToPathAsync(url, job.TargetPath, progress: progress, ct: ct, stallTimeout: _stallTimeout);
    }
}
