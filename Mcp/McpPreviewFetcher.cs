using System.Net;
using System.Net.Http;
using CanfarDesktop.Services;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.ViewState;

namespace CanfarDesktop.Mcp;

/// <summary>
/// Resolves a CADC observation's preview images (DataLink direct image files + <c>#preview</c> URLs) and
/// fetches one with an authenticated, redirect-following, size-bounded read. Extracted from
/// <see cref="McpToolCatalog"/> so the catalog stays close to pure wiring and this — its most intricate,
/// HTTP-heavy logic — is isolated and independently testable.
/// </summary>
public sealed class McpPreviewFetcher
{
    private readonly DataLinkService _dataLink;
    private readonly IHttpClientFactory _httpFactory;

    public McpPreviewFetcher(DataLinkService dataLink, IHttpClientFactory httpFactory)
    {
        _dataLink = dataLink;
        _httpFactory = httpFactory;
    }

    /// <summary>Resolve a CADC observation's preview images from DataLink (direct image files + #preview URLs).</summary>
    public async Task<IReadOnlyList<PreviewArtifact>> ResolveAsync(string publisherId, CancellationToken ct)
    {
        var links = await _dataLink.GetLinksAsync(publisherId, ct);
        var artifacts = new List<PreviewArtifact>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Direct files that declare an image content type carry the richest info.
        foreach (var file in links.DirectFiles)
        {
            if (!file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) continue;
            if (Uri.TryCreate(file.Url, UriKind.Absolute, out var uri) && seen.Add(file.Url))
                artifacts.Add(new PreviewArtifact(null, uri, file.ContentType, null,
                    string.IsNullOrEmpty(file.Filename) ? FileName(uri) : file.Filename));
        }

        // DataLink #preview URLs — images by definition (declared type guessed from the URL; magic bytes win at fetch).
        foreach (var url in links.Previews)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && seen.Add(url))
                artifacts.Add(new PreviewArtifact(null, uri, GuessImageType(uri), null, FileName(uri)));
        }

        return artifacts;
    }

    /// <summary>Authenticated, redirect-following, size-bounded image fetch. Throws typed PreviewFetchException.</summary>
    public async Task<PreviewBytes> FetchAsync(Uri url, int maxBytes, CancellationToken ct)
    {
        // The named client carries AuthTokenHandler (attaches the CADC token only to allow-listed hosts);
        // the redirect to signed minoc storage is pre-signed and needs no token.
        var client = _httpFactory.CreateClient("McpPreviewFetch");

        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new PreviewFetchException(new BackendError(ex.Message));
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new PreviewFetchException(new AuthRequired());
            if (!response.IsSuccessStatusCode)
                throw new PreviewFetchException(new BackendError($"HTTP {(int)response.StatusCode} fetching preview"));

            var declaredLength = response.Content.Headers.ContentLength;
            if (declaredLength is > 0 && declaredLength > maxBytes)
                throw new PreviewFetchException(new PreviewTooLarge((int)Math.Min(declaredLength.Value, int.MaxValue)));

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var buffer = new byte[maxBytes];
            var total = 0;
            while (total < maxBytes)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(total, maxBytes - total), ct);
                if (read == 0) break;
                total += read;
            }

            // One more byte means the body exceeded the cap.
            var extra = new byte[1];
            if (total == maxBytes && await stream.ReadAsync(extra.AsMemory(0, 1), ct) > 0)
                throw new PreviewFetchException(new PreviewTooLarge(maxBytes + 1));

            var data = total == buffer.Length ? buffer : buffer.AsSpan(0, total).ToArray();
            return new PreviewBytes(data, response.Content.Headers.ContentType?.MediaType);
        }
    }

    private static string FileName(Uri uri)
    {
        var name = System.IO.Path.GetFileName(uri.LocalPath);
        return string.IsNullOrEmpty(name) ? "preview" : name;
    }

    private static string GuessImageType(Uri uri) => System.IO.Path.GetExtension(uri.LocalPath).TrimStart('.').ToLowerInvariant() switch
    {
        "png" => "image/png",
        "gif" => "image/gif",
        "jpg" or "jpeg" => "image/jpeg",
        "webp" => "image/webp",
        "bmp" => "image/bmp",
        "tif" or "tiff" => "image/tiff",
        _ => "image/jpeg", // CADC previews are predominantly JPEG; magic bytes still decide the real type
    };
}
