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
        if (artifactIndex is int i)
        {
            if (i < 0 || i >= links.DirectFiles.Count)
                throw new ArgumentOutOfRangeException(nameof(artifactIndex),
                    $"artifactIndex {i} is out of range (0..{links.DirectFiles.Count - 1}); call list_observation_artifacts first");
            return links.DirectFiles[i].Url;
        }
        return links.DirectFileUrl ?? _dataLink.GetDownloadUrl(publisherId);
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

            await using (var stream = await response.Content.ReadAsStreamAsync(ct))
            await using (var fs = new FileStream(tmp, FileMode.Create))
            {
                var buffer = new byte[81920];
                long downloaded = 0;
                int read;
                while ((read = await stream.ReadAsync(buffer, ct)) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                    downloaded += read;
                    progress?.Report((downloaded, total));
                }
            }

            if (File.Exists(localPath)) File.Delete(localPath);
            File.Move(tmp, localPath);
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
            throw;
        }
    }
}
