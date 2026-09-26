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
        => (await ResolveFileAsync(publisherId, artifactIndex, ct)).Url;

    /// <summary>
    /// The file <see cref="ResolveUrlAsync"/> chooses, and which archive file it is when DataLink says
    /// (<see cref="DataLinkArtifactSelector.ArtifactIdOf"/>) — so a record of it can say what it holds.
    /// </summary>
    public async Task<(string Url, string? ArtifactId)> ResolveFileAsync(string publisherId, int? artifactIndex = null, CancellationToken ct = default)
    {
        var links = await _dataLink.GetLinksAsync(publisherId, ct);
        var url = DataLinkArtifactSelector.SelectUrl(links, artifactIndex, _dataLink.GetDownloadUrl(publisherId));
        return (url, DataLinkArtifactSelector.ArtifactIdOf(links, url));
    }

    /// <summary>
    /// The address of one particular archive file of an observation, by its name among the files DataLink
    /// lists — or null when DataLink lists none of that name.
    /// </summary>
    public async Task<string?> ResolveArtifactUrlAsync(string publisherId, string artifactId, CancellationToken ct = default)
    {
        var links = await _dataLink.GetLinksAsync(publisherId, ct);
        var name = Caom2Format.ArtifactFileName(artifactId);
        return links.DirectFiles.FirstOrDefault(f => string.Equals(f.Filename, name, StringComparison.OrdinalIgnoreCase))?.Url;
    }

    /// <summary>
    /// The SODA request for a cutout of an observation's file: the file's service from DataLink, the
    /// request from the spec. Throws when the file can no longer be cut, or the cutout no longer passes
    /// — a record kept for weeks meets today's descriptor, not the one it was cut from.
    /// </summary>
    public async Task<string> ResolveCutoutUrlAsync(string publisherId, Models.Cutouts.CutoutSpec spec, CancellationToken ct = default)
    {
        var links = await _dataLink.GetLinksAsync(publisherId, ct);
        var file = links.CutoutFor(spec.ArtifactId)
            ?? throw new InvalidOperationException($"{spec.ArtifactId} can no longer be cut out of {publisherId}");
        return Cutouts.SodaRequest.Url(file, spec);
    }

    /// <summary>
    /// Stream <paramref name="url"/> to <paramref name="localPath"/> atomically: write a sibling <c>.tmp</c>,
    /// then move it over the target. Reports (downloaded, total?) bytes to <paramref name="progress"/> when
    /// given; deletes the partial <c>.tmp</c> on any failure. Throws on a non-success HTTP status.
    ///
    /// <para><paramref name="timeoutSeconds"/> bounds only the wait for the response to START — the body
    /// streams after it with no limit of its own. <paramref name="stallTimeout"/> is what bounds the
    /// body: it fails a transfer that goes quiet, however long a healthy one runs.</para>
    /// </summary>
    public async Task DownloadToPathAsync(
        string url, string localPath, int timeoutSeconds = 120,
        IProgress<(long Downloaded, long? Total)>? progress = null, CancellationToken ct = default,
        TimeSpan? stallTimeout = null)
    {
        using var response = await _dataLink.DownloadAsync(url, timeoutSeconds);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);

        // A 200 that produced no bytes is not a download. The `pkg` endpoint answers exactly this for a
        // publisher id it cannot resolve, so refusing it here — before the file is put over the target
        // — leaves whatever was already there untouched and nothing new behind.
        await StreamToFile.WriteAsync(
            stream, localPath,
            expectedTotal: response.Content.Headers.ContentLength,
            progress: progress,
            validateTotal: written => written == 0
                ? new EmptyDownloadException(url, IsPackageEndpoint(url))
                : null,
            ct: ct,
            stallTimeout: stallTimeout);
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
