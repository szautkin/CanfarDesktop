using CanfarDesktop.Helpers;
using CanfarDesktop.Models.Cutouts;
using CanfarDesktop.Services.Fits;

namespace CanfarDesktop.Services.Cutouts.Local;

/// <summary>
/// Makes a cutout on this computer, from the observation's file already downloaded: found again each
/// time — a record kept for weeks is cut from the file as it is today — checked, planned and written
/// beside the target, then put in its place, so a cut that fails leaves nothing behind.
///
/// <para>The companions the cutout takes along — its weight map — are cut in the same job, with the
/// same boxes, into files beside it named by the same key (<see cref="CutoutSpec.CompanionPath"/>);
/// none is put in place unless all are written.</para>
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

            var file = LocalFitsFile.Inspect(path, job.Spec.ArtifactId, job.Spec.Companions);
            var check = new LocalCutoutSource(file).Check(job.Spec);
            if (!check.IsValid) throw new InvalidOperationException(check.Errors[0]);
            var plan = LocalCutPlan.For(file, job.Spec);
            var companions = plan.CompanionsOf(file, job.Spec);

            var writes = new List<(string Path, Action<Stream> Write)>();
            var total = plan.Bytes + companions.Sum(c => c.Plan.Bytes);
            long before = 0;
            foreach (var (from, cut, to) in companions
                         .Select(c => (c.Companion.File.Path, c.Plan, job.Spec.CompanionPath(job.TargetPath, c.Companion.ArtifactId)))
                         .Prepend((path, plan, job.TargetPath)))
            {
                if (SamePath(to, path) || companions.Any(c => SamePath(to, c.Companion.File.Path)))
                    throw new InvalidOperationException(CutoutRules.T("Cutout_LocalOverSource",
                        "A cutout cannot be saved over the file it is cut from."));
                var shifted = new Shifted(progress, before, total);
                writes.Add((to, destination =>
                {
                    using var source = FitsContainer.OpenFits(from);
                    FitsCutter.Write(source, cut, destination, shifted, ct);
                }));
                before += cut.Bytes;
            }

            stage.Report("cutting");
            AtomicFile.WriteStreams(writes);
        }, ct);

    private static bool SamePath(string a, string b)
        => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>One file's progress as part of the job's: after the bytes of the files before it, of all of them.</summary>
    private sealed class Shifted(IProgress<(long Done, long? Total)> progress, long before, long total) : IProgress<(long Done, long? Total)>
    {
        public void Report((long Done, long? Total) value) => progress.Report((before + value.Done, total));
    }
}
