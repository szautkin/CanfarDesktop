using System.Collections.Concurrent;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;

namespace CanfarDesktop.Services.Cutouts.Local;

/// <summary>
/// The observation's file on this computer as a way of cutting, read once for as long as it — and the
/// observation's files beside it — stay as they are. The observation view and Research ask for it each
/// time they are rebuilt, and reading it means reading its headers and the files beside it: for a
/// .fits.gz, inflating the whole file.
/// </summary>
public sealed class LocalSourceCache
{
    private readonly ConcurrentDictionary<string, (string Stamp, LocalCutoutSource Source)> _read = new();

    /// <summary>
    /// As <see cref="CutoutSources.Local"/> answers, from the last reading while nothing has changed.
    /// Reads the file when something has: run it off the UI.
    /// </summary>
    public LocalCutoutSource? Get(IEnumerable<DownloadedObservation> records, string publisherId, IReadOnlyCollection<string> artifactIds)
    {
        if (LocalCopies.CompleteFile(records, publisherId) is not { } record)
        {
            _read.TryRemove(publisherId, out _);
            return null;
        }

        var stamp = Stamp(record, artifactIds);
        if (stamp is not null && _read.TryGetValue(publisherId, out var known) && known.Stamp == stamp) return known.Source;

        var source = CutoutSources.Local([record], publisherId, artifactIds);
        if (stamp is not null && source is not null) _read[publisherId] = (stamp, source);
        return source;
    }

    /// <summary>
    /// What the file, and the observation's other files beside it, are now — which file, its size and
    /// time, which archive file it is, and which files were asked about — so that any change, a file
    /// downloaded, replaced or removed, reads it again. Null when the file cannot be looked at.
    /// </summary>
    public static string? Stamp(DownloadedObservation record, IEnumerable<string> artifactIds)
    {
        try
        {
            var file = new FileInfo(record.LocalPath);
            var ids = artifactIds.ToList();
            var beside = ids.Select(id => new FileInfo(Path.Combine(file.DirectoryName ?? "", Caom2Format.ArtifactFileName(id))))
                            .Where(f => f.Exists)
                            .Select(f => $"{f.Name}|{f.Length}|{f.LastWriteTimeUtc.Ticks}");
            return string.Join('/',
                [$"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}|{record.ArtifactId}|{string.Join(',', ids)}", .. beside]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
