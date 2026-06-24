using System.Net;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using CanfarDesktop.Models;
using CanfarDesktop.Services;
using CanfarDesktop.Services.Database;
using CanfarDesktop.Services.ImageDiscovery;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Builtin;
using CanfarDesktop.Mcp.Tools.Read;
using CanfarDesktop.Mcp.Tools.ViewState;
using CanfarDesktop.Mcp.Tools.Write;

namespace CanfarDesktop.Mcp;

/// <summary>
/// Builds the live MCP tool set by binding each pure tool to the app's real services (the tools take
/// injected delegates so they stay testable; this is the one place those delegates are wired to the
/// running DI graph). All tools are read-only (AgentSafe) — no tool here mutates state.
///
/// Deferred for a follow-up (need path/stream semantics an external agent can't readily drive):
/// the FITS header/WCS tools (operate on a local file path) and the VOSpace list/read tools.
/// Their omission is intentional, not a silent cap.
/// </summary>
public static class McpToolCatalog
{
    public static IReadOnlyList<IMcpTool> Build(IServiceProvider sp, string appVersion)
    {
        var auth = sp.GetRequiredService<IAuthService>();
        var observations = sp.GetRequiredService<ObservationStore>();
        var notes = sp.GetRequiredService<ObservationNoteStore>();
        var searchStore = sp.GetRequiredService<ISearchStoreService>();
        var tap = sp.GetRequiredService<ITAPService>();
        var sessions = sp.GetRequiredService<ISessionService>();
        var imageCatalog = sp.GetRequiredService<IImageService>();
        var recentLaunches = sp.GetRequiredService<IRecentLaunchService>();
        var discovery = sp.GetRequiredService<ImageDiscoveryCoordinator>();
        var caom2 = sp.GetRequiredService<ICAOM2Service>();
        var dataLink = sp.GetRequiredService<DataLinkService>();
        var viewState = sp.GetRequiredService<AppViewStateService>();
        var settings = sp.GetRequiredService<McpSettingsService>();
        var httpFactory = sp.GetRequiredService<IHttpClientFactory>();

        return new IMcpTool[]
        {
            // Foundational
            new DescribeAppTool(appVersion),
            new GetAuthStateTool(() => new AuthSnapshot(auth.IsAuthenticated, auth.CurrentUsername)),

            // Research (downloaded observations + notes)
            new ListDownloadedObservationsTool(() => observations.Observations),
            new GetDownloadedObservationTool(() => observations.Observations),
            new GetObservationNotesTool(() => notes.All()),

            // Saved search state
            new ListSavedQueriesTool(() => searchStore.LoadSavedQueries()),
            new ListRecentSearchesTool(() => searchStore.LoadRecentSearches()),

            // Live observation search (TAP / ADQL + name resolution)
            new SearchObservationsTool(
                (adql, max) => tap.ExecuteQueryAsync(adql, max),
                (target, service) => tap.ResolveTargetAsync(target, service)),
            new ResolveTargetTool((target, service) => tap.ResolveTargetAsync(target, service)),

            // Skaha sessions / headless jobs
            new ListSessionsTool(async () => (IReadOnlyList<Session>)await sessions.GetSessionsAsync()),
            new GetSessionTool(id => sessions.GetSessionAsync(id)),
            new ListHeadlessJobsTool(async () => (IReadOnlyList<Session>)await sessions.GetSessionsAsync()),
            new GetHeadlessJobLogsTool(id => sessions.GetSessionLogsAsync(id)),
            new GetHeadlessJobEventsTool(id => sessions.GetSessionEventsAsync(id)),

            // Image catalog + recent launches + package discovery
            new ListSessionImagesTool(() => imageCatalog.GetImagesAsync()),
            new ListRecentLaunchesTool(() => recentLaunches.Load()),
            new FindImagesWithPackagesTool(query => discovery.Search(query)),

            // CAOM2 metadata + DataLink (download/preview URLs)
            new GetObservationCaom2Tool(id => caom2.GetByPublisherIdAsync(id)),
            new GetDataLinksTool(id => dataLink.GetLinksAsync(id)),

            // View state: what the user is looking at + server-side preview fetch
            new GetCurrentViewTool(() =>
            {
                var v = viewState.Capture();
                return Task.FromResult(new AppViewSnapshot(
                    v.Mode, v.ModeTitle, auth.IsAuthenticated, auth.CurrentUsername ?? string.Empty,
                    v.SearchFocusRA, v.SearchFocusDec, v.OpenFitsPaths, settings.Enabled));
            }),
            new GetPreviewImageTool(
                publisherId => ResolvePreviewImagesAsync(dataLink, publisherId),
                (url, maxBytes) => FetchPreviewAsync(httpFactory, url, maxBytes)),

            // Proposal lifecycle: let the agent see + manage its queued write proposals
            new ListPendingProposalsTool(),
            new GetProposalStateTool(),
            new WithdrawProposalTool(),

            // Live ViewState writes: steer the user's view (no proposal)
            new NavigateToTool(mode => viewState.NavigateAsync(mode)),
            new SetSearchFocusTool((ra, dec) => viewState.SetSearchFocusActionAsync(ra, dec)),
        };
    }

    /// <summary>Resolve a CADC observation's preview images from DataLink (direct image files + #preview URLs).</summary>
    private static async Task<IReadOnlyList<PreviewArtifact>> ResolvePreviewImagesAsync(DataLinkService dataLink, string publisherId)
    {
        var links = await dataLink.GetLinksAsync(publisherId);
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
    private static async Task<PreviewBytes> FetchPreviewAsync(IHttpClientFactory factory, Uri url, int maxBytes)
    {
        // The named client carries AuthTokenHandler (attaches the CADC token only to allow-listed hosts);
        // the redirect to signed minoc storage is pre-signed and needs no token.
        var client = factory.CreateClient("McpPreviewFetch");

        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
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

            await using var stream = await response.Content.ReadAsStreamAsync();
            var buffer = new byte[maxBytes];
            var total = 0;
            while (total < maxBytes)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(total, maxBytes - total));
                if (read == 0) break;
                total += read;
            }

            // One more byte means the body exceeded the cap.
            var extra = new byte[1];
            if (total == maxBytes && await stream.ReadAsync(extra.AsMemory(0, 1)) > 0)
                throw new PreviewFetchException(new PreviewTooLarge(maxBytes + 1));

            var data = total == buffer.Length ? buffer : buffer.AsSpan(0, total).ToArray();
            return new PreviewBytes(data, response.Content.Headers.ContentType?.MediaType);
        }
    }

    private static string FileName(Uri uri)
    {
        var name = Path.GetFileName(uri.LocalPath);
        return string.IsNullOrEmpty(name) ? "preview" : name;
    }

    private static string GuessImageType(Uri uri) => Path.GetExtension(uri.LocalPath).TrimStart('.').ToLowerInvariant() switch
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
