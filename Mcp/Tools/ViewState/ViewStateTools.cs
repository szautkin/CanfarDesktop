using System.Text;
using System.Text.Json;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Mcp.Tools.ViewState;

// ─────────────────────────────────────────────────────────────────────────────
// View-state tools (both read-only / agent-safe), 1-to-1 with the macOS
// GetCurrentViewTool + GetPreviewImageTool. get_current_view returns a structured
// snapshot of what the user is looking at; get_preview_image fetches a CADC
// observation PREVIEW server-side and returns it as an inline image block.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>What the user is currently looking at — the output of <c>get_current_view</c>.</summary>
public sealed record AppViewSnapshot(
    string Mode,
    string ModeTitle,
    bool IsAuthenticated,
    string Username,
    double? SearchFocusRA,
    double? SearchFocusDec,
    IReadOnlyList<string> OpenFitsPaths,
    bool AgentsEnabled);

/// <summary>
/// <c>get_current_view</c> — return navigation state + light per-mode context so an agent can reason
/// in the user's current frame. Bodies/payloads are never exposed — only mode, auth, the Search form's
/// sky focus, and the open FITS paths. The host supplies a live snapshot via the injected delegate.
/// </summary>
public sealed class GetCurrentViewTool : JsonReadTool<EmptyArgs, AppViewSnapshot>
{
    private const string Schema = """{"type":"object","properties":{},"additionalProperties":false}""";

    private readonly Func<Task<AppViewSnapshot>> _snapshot;

    public GetCurrentViewTool(Func<Task<AppViewSnapshot>> snapshot) => _snapshot = snapshot;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_current_view",
        "Return what the user is currently looking at: the mode (landing/search/research/portal/storage/" +
        "fitsViewer), auth state + username, the Search form's sky focus (RA/Dec) when in Search, and the " +
        "open FITS file paths when in the FITS viewer. Read-only; lets you reason in the user's context.",
        Schema);

    protected override Task<AppViewSnapshot> HandleAsync(EmptyArgs args, McpToolContext context, CancellationToken ct)
        => _snapshot();
}

/// <summary>A preview artifact resolved from DataLink/CAOM-2 (band is null when unknown).</summary>
public sealed record PreviewArtifact(string? Band, Uri Url, string? ContentType, long? ContentLength, string Filename);

/// <summary>Bytes returned by the injected, auth'd, redirect-following fetcher.</summary>
public sealed record PreviewBytes(byte[] Data, string? ContentType);

/// <summary>Thrown by the injected fetcher to surface a typed failure (auth/size/transport).</summary>
public sealed class PreviewFetchException : Exception
{
    public ToolFailureReason Reason { get; }
    public PreviewFetchException(ToolFailureReason reason) : base(reason.Description) => Reason = reason;
}

/// <summary>
/// <c>get_preview_image</c> — fetch a CADC observation's PREVIEW image server-side (the host has CADC
/// reach + the user's auth; the agent sandbox blocks CADC hosts) and return it as an inline image plus
/// a lean JSON metadata caption. Resolves to an image preview, never a science frame; caps the size so
/// the base64 response stays under MCP clients' ~1 MB body limit. The CAOM-2/DataLink resolution and the
/// authenticated, redirect-following byte fetch are injected so the tool is pure/testable.
/// </summary>
public sealed class GetPreviewImageTool : IMcpTool
{
    /// <summary>MCP clients reject a response body larger than ~1 MB; the image ships as base64 (~4/3).</summary>
    public const int McpResponseByteLimit = 1_048_576;
    /// <summary>Default/maximum raw image size (≈696 KB → ~928 KB base64).</summary>
    public const int DefaultMaxBytes = 680 * 1024;

    private readonly Func<string, Task<IReadOnlyList<PreviewArtifact>>> _resolvePreviews;
    private readonly Func<Uri, int, Task<PreviewBytes>> _fetchImage;

    public GetPreviewImageTool(
        Func<string, Task<IReadOnlyList<PreviewArtifact>>> resolvePreviews,
        Func<Uri, int, Task<PreviewBytes>> fetchImage)
    {
        _resolvePreviews = resolvePreviews;
        _fetchImage = fetchImage;
    }

    public McpVerbClass VerbClass => McpVerbClass.Read;
    public bool AgentSafe => true;

    private static TimeSpan Timeout => TimeSpan.FromSeconds(30);

    public ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_preview_image",
        "Fetch a CADC observation's PREVIEW image server-side (using the user's CADC auth, following the " +
        "redirect to signed storage) and return it as an inline image you can show the user, plus a small " +
        "JSON metadata caption (filename, band, byteSize, sourceUrl, contentType). Use the same publisherId " +
        "as get_data_links / get_observation_caom2. Pass `band` to pick a band for multi-band observations; " +
        "omit for the default preview. Resolves only image previews, never a science frame. `maxBytes` " +
        "(default ~680KB, hard-capped) refuses a mis-resolved giant file and keeps the response under the " +
        "~1 MB client limit. Typed failures: preview_not_found (lists bands that have previews), " +
        "preview_too_large, auth_required (proprietary/embargoed), timeout, content_type_mismatch.",
        """{"type":"object","required":["publisherId"],"properties":{"publisherId":{"type":"string"},"band":{"type":"string"},"maxBytes":{"type":"integer","minimum":1}},"additionalProperties":false}""");

    public async Task<ToolResult> InvokeAsync(JsonValue arguments, McpToolContext context, CancellationToken cancellationToken)
    {
        Args args;
        try { args = DeserializeArgs(arguments); }
        catch (JsonException ex) { return ToolResult.Fail(new InvalidArgument(ex.Message)); }

        var publisherId = (args.PublisherId ?? string.Empty).Trim();
        if (publisherId.Length == 0) return ToolResult.Fail(new InvalidArgument("publisherId is required"));
        if (args.MaxBytes is <= 0) return ToolResult.Fail(new InvalidArgument("maxBytes must be a positive integer"));

        var maxBytes = Math.Min(Math.Max(1, args.MaxBytes ?? DefaultMaxBytes), DefaultMaxBytes);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(Timeout);
            return await RunAsync(publisherId, args.Band, maxBytes, cts.Token);
        }
        catch (McpToolException ex) { return ToolResult.Fail(ex.Reason); }
        catch (PreviewFetchException ex) { return ToolResult.Fail(ex.Reason); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return ToolResult.Fail(new UpstreamTimeout()); }
        catch (Exception ex) { return ToolResult.Fail(new BackendError(ex.Message)); }
    }

    private async Task<ToolResult> RunAsync(string publisherId, string? band, int maxBytes, CancellationToken ct)
    {
        // 1. Resolve preview artifacts; keep only image previews — never a science frame.
        var artifacts = await _resolvePreviews(publisherId);
        var previews = artifacts
            .Where(a => ImageMagic.IsImageContentType(a.ContentType) || ImageMagic.HasImageExtension(a.Filename))
            .ToList();
        if (previews.Count == 0)
            throw new McpToolException(new PreviewNotFound($"no preview images for {publisherId}"));

        // 2. Pick by band, or the default (first) preview.
        PreviewArtifact chosen;
        var wantBand = band?.Trim();
        if (!string.IsNullOrEmpty(wantBand))
        {
            var match = previews.FirstOrDefault(p => string.Equals(p.Band, wantBand, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                var bands = previews.Where(p => p.Band is not null).Select(p => p.Band!).Distinct().OrderBy(b => b).ToList();
                var list = bands.Count == 0 ? "(none of the previews declare a band)" : string.Join(", ", bands);
                throw new McpToolException(new PreviewNotFound($"no preview for band '{wantBand}'. Available preview bands: {list}"));
            }
            chosen = match;
        }
        else
        {
            chosen = previews[0];
        }

        // 3. Cap by declared Content-Length before transferring the body.
        if (chosen.ContentLength is { } len && len > maxBytes)
            throw new McpToolException(new PreviewTooLarge((int)Math.Min(len, int.MaxValue)));

        // 4. Fetch the bytes server-side (auth'd, follows redirects). Typed failures via PreviewFetchException.
        var fetched = await _fetchImage(chosen.Url, maxBytes);

        // 5. Verify the bytes are actually an image (magic bytes authoritative; declared image/* otherwise,
        //    unless the payload looks like a text/markup error body shipped with an image content type).
        var mime = ImageMagic.ResolveMime(fetched.Data, fetched.ContentType ?? chosen.ContentType);
        if (mime is null)
            throw new McpToolException(new ContentTypeMismatch(
                $"fetched {fetched.Data.Length} bytes that are not a recognised image (declared {fetched.ContentType ?? chosen.ContentType ?? "?"})"));

        // 6. Safety net against an over-sending server so the base64 response can't exceed the client limit.
        if (fetched.Data.Length > DefaultMaxBytes)
            throw new McpToolException(new PreviewTooLarge(fetched.Data.Length));

        // 7. Inline image + lean JSON caption (the image rides in the image block, not duplicated as base64).
        return ToolResult.ImageResult(fetched.Data, mime, BuildCaption(chosen, fetched.Data.Length, mime));
    }

    private static string BuildCaption(PreviewArtifact a, int byteSize, string mime)
        => JsonSerializer.Serialize(
            new { filename = a.Filename, band = a.Band, byteSize, sourceUrl = a.Url.ToString(), contentType = mime },
            McpJson.Options);

    private static Args DeserializeArgs(JsonValue arguments)
    {
        if (arguments is JsonNull) return new Args();
        return JsonSerializer.Deserialize<Args>(arguments.ToJsonString(), McpJson.Options) ?? new Args();
    }

    public sealed record Args
    {
        public string? PublisherId { get; init; }
        public string? Band { get; init; }
        public int? MaxBytes { get; init; }
    }
}

/// <summary>Image typing by magic bytes (authoritative) with a guarded fallback to a declared content type.</summary>
public static class ImageMagic
{
    private static readonly string[] ImageExtensions = { "gif", "png", "jpg", "jpeg", "webp", "bmp", "tif", "tiff", "jp2" };

    public static bool IsImageContentType(string? type)
        => (type ?? string.Empty).StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    public static bool HasImageExtension(string filename)
    {
        var ext = Path.GetExtension(filename).TrimStart('.').ToLowerInvariant();
        return ImageExtensions.Contains(ext);
    }

    /// <summary>
    /// Resolve the image mime for the bytes: known magic bytes win; otherwise accept a declared
    /// <c>image/*</c> type unless the payload looks like a text/markup error body. Null when not an image.
    /// </summary>
    public static string? ResolveMime(byte[] data, string? declared)
    {
        if (StartsWith(data, 0x47, 0x49, 0x46, 0x38)) return "image/gif";                               // "GIF8"
        if (StartsWith(data, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)) return "image/png";
        if (StartsWith(data, 0xFF, 0xD8, 0xFF)) return "image/jpeg";
        if (data.Length >= 12 && StartsWith(data, 0x52, 0x49, 0x46, 0x46)                               // "RIFF"…"WEBP"
            && data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50) return "image/webp";
        if (StartsWith(data, 0x42, 0x4D)) return "image/bmp";                                            // "BM"
        if (StartsWith(data, 0x49, 0x49, 0x2A, 0x00) || StartsWith(data, 0x4D, 0x4D, 0x00, 0x2A)) return "image/tiff";

        if (declared is not null
            && declared.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            && !LooksLikeTextErrorBody(data))
            return declared.ToLowerInvariant();

        return null;
    }

    /// <summary>True when bytes are almost certainly a text/HTML/JSON error body, not an image.</summary>
    public static bool LooksLikeTextErrorBody(byte[] data)
    {
        if (data.Length >= 8192) return false;            // a large body is plausibly an image
        if (data.Length > 0 && data[0] == 0x3C) return true; // '<' → HTML/XML/VOTable error
        try
        {
            var text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(data).Trim();
            return true;                                  // decodes cleanly as modest UTF-8 → treat as an error string
        }
        catch (DecoderFallbackException)
        {
            return false;                                 // invalid UTF-8 → binary (real image)
        }
    }

    private static bool StartsWith(byte[] data, params int[] prefix)
    {
        if (data.Length < prefix.Length) return false;
        for (var i = 0; i < prefix.Length; i++)
            if (data[i] != (byte)prefix[i]) return false;
        return true;
    }
}
