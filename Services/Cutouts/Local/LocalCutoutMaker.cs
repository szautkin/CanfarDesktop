using CanfarDesktop.Helpers;
using CanfarDesktop.Models.Cutouts;
using CanfarDesktop.Services.Fits;

namespace CanfarDesktop.Services.Cutouts.Local;

/// <summary>
/// Makes a cutout on this computer, from the observation's file already downloaded: found again each
/// time — a record kept for weeks is cut from the file as it is today — checked, planned and written
/// beside the target, then put in its place, so a cut that fails leaves nothing behind.
/// </summary>
public sealed class LocalCutoutMaker : ICutoutMaker
{
    private readonly Func<string, string?, string?> _findFile;

    /// <param name="findFile">Where the observation's (publisher id) archive file (artifact id) is on this computer, or null.</param>
    public LocalCutoutMaker(Func<string, string?, string?> findFile) => _findFile = findFile;

    public CutoutMethod Method => CutoutMethod.Local;

    public string TaskLabel(string fileName) => $"Cut out {fileName}";

    public Task MakeAsync(CutoutJob job, IProgress<string> stage, IProgress<(long Done, long? Total)> progress,
                          CancellationToken ct = default)
        => Task.Run(() =>
        {
            stage.Report("reading the file");
            var path = _findFile(job.PublisherId, job.Spec.ArtifactId)
                ?? throw new InvalidOperationException(CutoutRules.T("Cutout_LocalSourceGone",
                    "The file it is cut from is not on this computer any more. Download the observation's file, then try again."));
            if (SamePath(path, job.TargetPath))
                throw new InvalidOperationException(CutoutRules.T("Cutout_LocalOverSource",
                    "A cutout cannot be saved over the file it is cut from."));

            var file = LocalFitsFile.Inspect(path, job.Spec.ArtifactId);
            var check = new LocalCutoutSource(file).Check(job.Spec);
            if (!check.IsValid) throw new InvalidOperationException(check.Errors[0]);
            var plan = LocalCutPlan.For(file, job.Spec);

            stage.Report("cutting");
            using var source = FitsContainer.OpenFits(path);
            AtomicFile.WriteStream(job.TargetPath, destination => FitsCutter.Write(source, plan, destination, progress, ct));
        }, ct);

    private static bool SamePath(string a, string b)
        => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
