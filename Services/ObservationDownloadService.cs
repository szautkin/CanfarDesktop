using CanfarDesktop.Helpers;

namespace CanfarDesktop.Services;

/// <summary>
/// Resolves an observation's download URL and streams it to a local path atomically (write a sibling
/// <c>.tmp</c>, then move it over the target). One canonical place for the resolve + download core that
/// the MCP download tool, the Research view, and the Search view all shared.
/// </summary>
public sealed class ObservationDownloadService
{
    private readonly DataLinkService _dataLink;
    public ObservationDownloadService(DataLinkService dataLink) => _dataLink = dataLink;

    /// <summary>
    /// Download URL for an observation. With <paramref name="artifactIndex"/> set, picks that specific
    /// DataLink artifact (from <c>list_observation_artifacts</c>); otherwise the primary direct file, else
    /// the DataLink download URL. Throws when the index is out of range.
    /// </summary>
    public async Task<string> ResolveUrlAsync(string publisherId, int? artifactIndex = null, CancellationToken ct = default)
    {
        var links = await _dataLink.GetLinksAsync(publisherId, ct);
        return DataLinkArtifactSelector.SelectUrl(links, artifactIndex, _dataLink.GetDownloadUrl(publisherId));
    }

    /// <summary>
    /// Stream <paramref name="url"/> to <paramref name="localPath"/> atomically: write a sibling <c>.tmp</c>,
    /// then move it over the target. Reports (downloaded, total?) bytes to <paramref name="progress"/> when
    /// given; deletes the partial <c>.tmp</c> on any failure. Throws on a non-success HTTP status.
    /// </summary>
    public async Task DownloadToPathAsync(
        string url, string localPath, int timeoutSeconds = 120,
        IProgress<(long Downloaded, long? Total)>? progress = null, CancellationToken ct = default)
    {
        var tmp = localPath + ".tmp";
        try
        {
            using var response = await _dataLink.DownloadAsync(url, timeoutSeconds);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;

            long downloaded = 0;
            await using (var stream = await response.Content.ReadAsStreamAsync(ct))
            await using (var fs = new FileStream(tmp, FileMode.Create))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(buffer, ct)) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                    downloaded += read;
                    progress?.Report((downloaded, total));
                }
            }

            // A 200 that produced no bytes is not a download. The `pkg` endpoint answers exactly this
            // for a publisher id it cannot resolve, so the empty file is removed rather than filed.
            if (downloaded == 0)
                throw new EmptyDownloadException(url, IsPackageEndpoint(url));

            if (File.Exists(localPath)) File.Delete(localPath);
            File.Move(tmp, localPath);
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
            throw;
        }
    }

    /// <summary>
    /// True when the URL is the <c>caom2ops/pkg</c> fallback rather than a resolved DataLink artifact.
    /// It decides which of the two empty-response messages is the honest one: a package endpoint that
    /// returned nothing almost always means the id did not resolve, while an empty DataLink artifact
    /// is an empty artifact.
    /// </summary>
    internal static bool IsPackageEndpoint(string url)
        => url.Contains("caom2ops/pkg", StringComparison.OrdinalIgnoreCase)
        || url.Contains("/pkg?", StringComparison.OrdinalIgnoreCase);
}
