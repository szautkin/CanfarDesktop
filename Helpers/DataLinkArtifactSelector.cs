using CanfarDesktop.Models;

namespace CanfarDesktop.Helpers;

/// <summary>
/// Pure selection of a download URL from a DataLink result (SCI-5): an explicit <c>artifactIndex</c>
/// picks that specific direct file (bounds-checked); otherwise the primary direct file, else the
/// supplied fallback URL. Extracted from <see cref="Services.ObservationDownloadService"/> so the
/// index contract is unit-testable without any HTTP.
/// </summary>
public static class DataLinkArtifactSelector
{
    /// <summary>
    /// Resolve the download URL. With <paramref name="artifactIndex"/> set, returns that direct file's
    /// URL (throwing <see cref="ArgumentOutOfRangeException"/> when out of range); otherwise the first
    /// direct file, else <paramref name="fallbackUrl"/>.
    /// </summary>
    public static string SelectUrl(DataLinkResult links, int? artifactIndex, string fallbackUrl)
    {
        ArgumentNullException.ThrowIfNull(links);
        if (artifactIndex is int i)
        {
            if (i < 0 || i >= links.DirectFiles.Count)
                throw new ArgumentOutOfRangeException(nameof(artifactIndex),
                    $"artifactIndex {i} is out of range (0..{links.DirectFiles.Count - 1}); call list_observation_artifacts first");
            return links.DirectFiles[i].Url;
        }
        return links.DirectFileUrl ?? fallbackUrl;
    }
}
