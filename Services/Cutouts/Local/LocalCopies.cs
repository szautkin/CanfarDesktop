using CanfarDesktop.Helpers;
using CanfarDesktop.Models;

namespace CanfarDesktop.Services.Cutouts.Local;

/// <summary>
/// Which of an observation's archive files is on this computer, whole — the one question a local cut
/// asks before anything else, answered here for the observation view, Research, an agent and the cut
/// itself alike.
///
/// <para>Research keeps one complete record per observation, so there is at most one such file. Which
/// archive file it is, is what it was downloaded as (<see cref="DownloadedObservation.ArtifactId"/>);
/// for a record from before that was kept, the archive file whose name it has; otherwise not known —
/// and then it is still the observation's downloaded file, just not tied to one of its files.</para>
/// </summary>
public static class LocalCopies
{
    /// <summary>The observation's complete file on this computer: Research's record of it, when the file is there.</summary>
    public static DownloadedObservation? CompleteFile(IEnumerable<DownloadedObservation> records, string publisherId)
        => ResearchRecords.Complete(records, publisherId) is { FileExists: true } record ? record : null;

    /// <summary>Which of <paramref name="artifactIds"/> the record's file is; empty when none can be said to be.</summary>
    public static string ArtifactOf(DownloadedObservation record, IEnumerable<string> artifactIds)
        => record.ArtifactId is { Length: > 0 } known
            ? known
            : artifactIds.FirstOrDefault(id => NameMatches(record, id)) ?? string.Empty;

    /// <summary>
    /// Whether the record's file is this archive file. An empty <paramref name="artifactId"/> means the
    /// observation's downloaded file, whichever it is.
    /// </summary>
    public static bool Is(DownloadedObservation record, string? artifactId)
        => string.IsNullOrEmpty(artifactId)
           || (record.ArtifactId is { Length: > 0 } known ? known == artifactId : NameMatches(record, artifactId));

    /// <summary>The file on this computer to cut this archive file from, or null when it is not here.</summary>
    public static DownloadedObservation? Find(IEnumerable<DownloadedObservation> records, string publisherId, string? artifactId)
        => CompleteFile(records, publisherId) is { } record && Is(record, artifactId) ? record : null;

    private static bool NameMatches(DownloadedObservation record, string artifactId)
        => string.Equals(record.Filename, Caom2Format.ArtifactFileName(artifactId), StringComparison.OrdinalIgnoreCase);
}
