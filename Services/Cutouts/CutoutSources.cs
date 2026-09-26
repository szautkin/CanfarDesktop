using CanfarDesktop.Models;
using CanfarDesktop.Models.Caom2;
using CanfarDesktop.Models.Cutouts;
using CanfarDesktop.Services.Cutouts.Local;

namespace CanfarDesktop.Services.Cutouts;

/// <summary>
/// The ways an observation's files can be cut, gathered in one place: the observation view's Files tab,
/// Research, a mark's menu and an agent's tools all ask here, so none of them can offer a way the
/// others do not — or prefer one the others do not.
/// </summary>
public static class CutoutSources
{
    /// <summary>
    /// The files CADC can cut, from the observation's DataLink answer, each with the whole file's size
    /// from CAOM2 when it is known.
    /// </summary>
    public static IReadOnlyList<ICutoutSource> Soda(DataLinkResult links, CAOM2Observation? observation)
        => links.Cutouts.Select(d => (ICutoutSource)new SodaCutoutSource(d, SizeOf(observation, d.ArtifactId))).ToList();

    /// <summary>
    /// The observation's file on this computer, as a way of cutting — tied to whichever of
    /// <paramref name="artifactIds"/> it is, with those of the rest beside it that can be cut with it —
    /// or null when Research has none of it here. Reads the files' headers: run it off the UI.
    /// </summary>
    public static LocalCutoutSource? Local(IEnumerable<DownloadedObservation> records, string publisherId, IEnumerable<string> artifactIds)
    {
        if (LocalCopies.CompleteFile(records, publisherId) is not { } record) return null;
        var ids = artifactIds.ToList();
        return new LocalCutoutSource(LocalFitsFile.Inspect(record.LocalPath, LocalCopies.ArtifactOf(record, ids), ids));
    }

    /// <summary>Every archive file an observation has, by its CAOM2 URI.</summary>
    public static IEnumerable<string> ArtifactIds(CAOM2Observation? observation)
        => observation?.Planes.SelectMany(p => p.Artifacts).Select(a => a.Uri).Where(u => u.Length > 0) ?? [];

    /// <summary>An archive file's size, as CAOM2 states it.</summary>
    public static long? SizeOf(CAOM2Observation? observation, string artifactId)
        => observation?.Planes.SelectMany(p => p.Artifacts).FirstOrDefault(a => a.Uri == artifactId)?.ContentLength;

    /// <summary>The ways one file can be cut, from all of an observation's, best first.</summary>
    public static IReadOnlyList<ICutoutSource> For(IEnumerable<ICutoutSource> sources, string? artifactId)
        => Preferred(sources.Where(s => string.Equals(s.File.ArtifactId, artifactId ?? string.Empty, StringComparison.Ordinal)));

    /// <summary>
    /// The ways in the order they are offered: one that can cut before one that cannot; then the file on
    /// this computer — instant, offline, and no load on CADC — before CADC's; unless
    /// <paramref name="prefer"/> names one.
    /// </summary>
    public static IReadOnlyList<ICutoutSource> Preferred(IEnumerable<ICutoutSource> ways, CutoutMethod? prefer = null)
        => ways.OrderBy(w => w.Unavailable is null ? 0 : 1)
               .ThenBy(w => prefer is { } p ? (w.Method == p ? 0 : 1) : 0)
               .ThenBy(w => w.Method == CutoutMethod.Local ? 0 : 1)
               .ToList();
}
