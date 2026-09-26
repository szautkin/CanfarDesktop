using CanfarDesktop.Models;
using CanfarDesktop.Models.Caom2;

namespace CanfarDesktop.Services.Cutouts;

/// <summary>
/// The ways an observation's files can be cut, gathered in one place: the observation view's Files tab,
/// Research, a mark's menu and an agent's tools all ask here, so none of them can offer a way the
/// others do not.
/// </summary>
public static class CutoutSources
{
    /// <summary>
    /// The files CADC can cut, from the observation's DataLink answer, each with the whole file's size
    /// from CAOM2 when it is known.
    /// </summary>
    public static IReadOnlyList<ICutoutSource> Soda(DataLinkResult links, CAOM2Observation? observation)
        => links.Cutouts.Select(d => (ICutoutSource)new SodaCutoutSource(d, SizeOf(observation, d.ArtifactId))).ToList();

    /// <summary>An archive file's size, as CAOM2 states it.</summary>
    public static long? SizeOf(CAOM2Observation? observation, string artifactId)
        => observation?.Planes.SelectMany(p => p.Artifacts).FirstOrDefault(a => a.Uri == artifactId)?.ContentLength;

    /// <summary>The ways one file can be cut, from all of an observation's.</summary>
    public static IReadOnlyList<ICutoutSource> For(IEnumerable<ICutoutSource> sources, string? artifactId)
        => sources.Where(s => string.Equals(s.File.ArtifactId, artifactId, StringComparison.Ordinal)).ToList();
}
