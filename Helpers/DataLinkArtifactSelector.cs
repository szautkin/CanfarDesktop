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

    /// <summary>
    /// Which of several <c>#this</c> rows is the science product.
    ///
    /// A <c>#this</c> row marks a science product and most collections publish exactly one, so taking
    /// the first was right everywhere it was tested. JWST publishes several: four of six planes sampled
    /// put a four-kilobyte <c>_asn.json</c> association index ahead of the 46 MB <c>_i2d.fits</c> image,
    /// so "download this observation" fetched the index and reported success.
    ///
    /// Ranked rather than filtered — an unrecognised row is still a candidate, because being unable to
    /// classify a product is not a reason to refuse to download it. Ties keep the service's own order.
    /// </summary>
    public static DataLinkFile? PreferScienceFile(IReadOnlyList<DataLinkFile> files)
    {
        if (files is null || files.Count == 0) return null;
        if (files.Count == 1) return files[0];

        DataLinkFile? best = null;
        var bestRank = int.MinValue;
        foreach (var f in files)
        {
            var rank = Rank(f);
            if (rank > bestRank) { bestRank = rank; best = f; }
        }
        return best;
    }

    /// <summary>Higher is more likely to be the image someone asked for.</summary>
    private static int Rank(DataLinkFile file)
    {
        var name = file.Filename;
        if (IsExtension(name, ".fits") || IsExtension(name, ".fits.fz") || IsExtension(name, ".fz")) return 2;
        if (file.ContentType.Contains("fits", StringComparison.OrdinalIgnoreCase)) return 2;

        // An association index, a catalogue sidecar or a log: real products, rarely the one meant.
        if (IsExtension(name, ".json") || IsExtension(name, ".xml") || IsExtension(name, ".csv")
            || IsExtension(name, ".txt") || IsExtension(name, ".log")) return -1;

        return 0;
    }

    private static bool IsExtension(string name, string ext) =>
        name.EndsWith(ext, StringComparison.OrdinalIgnoreCase);
}
