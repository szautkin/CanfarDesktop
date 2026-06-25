using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Models.Fits;
using CanfarDesktop.Services;
using CanfarDesktop.Services.Database;
using CanfarDesktop.Services.Fits;
using CanfarDesktop.Services.ImageDiscovery;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Builtin;
using CanfarDesktop.Mcp.Tools.Proposals;
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
        var storage = sp.GetRequiredService<IStorageService>();
        var platform = sp.GetRequiredService<IPlatformService>();
        var endpoints = sp.GetRequiredService<ApiEndpoints>();
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
            new GetSavedQueryTool(() => searchStore.LoadSavedQueries()),
            new ListRecentSearchesTool(() => searchStore.LoadRecentSearches()),

            // Live observation search (TAP / ADQL + name resolution)
            new SearchObservationsTool(
                (adql, max, ct) => tap.ExecuteQueryAsync(adql, max, ct),
                (target, service, ct) => tap.ResolveTargetAsync(target, service, ct)),
            new ResolveTargetTool((target, service, ct) => tap.ResolveTargetAsync(target, service, ct)),

            // Skaha sessions / headless jobs
            new ListSessionsTool(async ct => (IReadOnlyList<Session>)await sessions.GetSessionsAsync(ct)),
            new GetSessionTool((id, ct) => sessions.GetSessionAsync(id, ct)),
            new ListSessionTypesTool(),
            new ListHeadlessJobsTool(async ct => (IReadOnlyList<Session>)await sessions.GetSessionsAsync(ct)),
            new GetHeadlessJobLogsTool((id, ct) => sessions.GetSessionLogsAsync(id, ct)),
            new GetHeadlessJobEventsTool((id, ct) => sessions.GetSessionEventsAsync(id, ct)),

            // Image catalog + recent launches + package discovery
            new ListSessionImagesTool(ct => imageCatalog.GetImagesAsync(ct)),
            new ListRecentLaunchesTool(() => recentLaunches.Load()),
            new FindImagesWithPackagesTool(query => discovery.Search(query)),

            // CAOM2 metadata + DataLink (download/preview URLs)
            new GetObservationCaom2Tool((id, ct) => caom2.GetByPublisherIdAsync(id, ct)),
            new GetDataLinksTool((id, ct) => dataLink.GetLinksAsync(id, ct)),

            // VOSpace/ARC storage (read) + local FITS introspection
            new ListVoSpacePathTool((req, ct) => storage.ListNodesAsync(req.Path, req.Limit, ct)),
            new ReadVoSpaceFileTool((path, ct) => storage.DownloadFileAsync(path, ct)),
            new GetStorageQuotaTool(ct => storage.GetQuotaAsync(auth.CurrentUsername ?? string.Empty, ct)),
            new GetFitsHeaderTool(ParseFitsHeadersAsync),
            new GetFitsWcsTool(ParseFitsHeadersAsync),

            // Platform load + upstream service health
            new GetPlatformLoadTool(ct => platform.GetStatsAsync(ct)),
            new GetServiceHealthTool(() => ProbeServicesAsync(httpFactory, endpoints)),

            // View state: what the user is looking at + autonomy/budget + server-side preview fetch
            new GetCurrentViewTool(ctx =>
            {
                var v = viewState.Capture();
                var pending = ctx.Proposals?.List().Count ?? 0;
                var cap = ctx.Budget?.Limit ?? 0;
                var remaining = ctx.Budget?.Remaining(ctx.Origin) ?? 0;
                return Task.FromResult(new AppViewSnapshot(
                    v.Mode, v.ModeTitle, auth.IsAuthenticated, auth.CurrentUsername ?? string.Empty,
                    v.SearchFocusRA, v.SearchFocusDec, v.OpenFitsPaths,
                    settings.Enabled, settings.AutoApplyEnabled, settings.FollowAgentActivityEnabled,
                    pending, new BudgetSnapshot(cap, remaining)));
            }),
            new GetPreviewImageTool(
                (publisherId, ct) => ResolvePreviewImagesAsync(dataLink, publisherId, ct),
                (url, maxBytes, ct) => FetchPreviewAsync(httpFactory, url, maxBytes, ct)),

            // Proposal lifecycle: let the agent see + manage its queued write proposals
            new ListPendingProposalsTool(),
            new GetProposalStateTool(),
            new WithdrawProposalTool(),

            // Live ViewState writes: steer the user's view (no proposal)
            new NavigateToTool(mode => viewState.NavigateAsync(mode)),
            new SetSearchFocusTool((ra, dec) => viewState.SetSearchFocusActionAsync(ra, dec)),
            new OpenFitsFileTool(id => viewState.OpenFitsAsync(id)),

            // Semantic writes (proposals; auto-apply or queue per the autonomy toggle)
            new SaveQueryTool(),
            new DeleteSavedQueryTool(),
            new UpdateObservationNoteTool(),
            new BulkUpdateObservationNotesTool(),

            // Skaha session lifecycle
            new LaunchSessionTool(),
            new LaunchHeadlessJobTool(),
            new DeleteSessionTool(),
            new RenewSessionTool(),

            // Research: download / remove observations
            new DownloadObservationTool(),
            new DeleteDownloadedObservationTool(),

            // VOSpace/ARC storage writes
            new UploadTextToVoSpaceTool(),
            new CreateVoSpaceFolderTool(),
            new DeleteVoSpaceNodeTool(),
        };
    }

    /// <summary>Build the proposal appliers bound to the live stores (registered by the host).</summary>
    public static IReadOnlyList<IProposalApplier> BuildAppliers(IServiceProvider sp)
    {
        var searchStore = sp.GetRequiredService<ISearchStoreService>();
        var noteStore = sp.GetRequiredService<ObservationNoteStore>();
        var sessions = sp.GetRequiredService<ISessionService>();
        var observations = sp.GetRequiredService<ObservationStore>();
        var dataLink = sp.GetRequiredService<DataLinkService>();
        var storage = sp.GetRequiredService<IStorageService>();

        return new IProposalApplier[]
        {
            new SaveQueryApplier(payload =>
            {
                searchStore.SaveQuery(new SavedQuery { Name = payload.Name, Adql = payload.Adql, SavedAt = DateTime.UtcNow });
                return Task.CompletedTask;
            }),
            new DeleteSavedQueryApplier(payload =>
            {
                searchStore.DeleteQuery(payload.Name);
                return Task.CompletedTask;
            }),
            new UpdateObservationNoteApplier(payload =>
            {
                ApplyNote(noteStore, payload);
                return Task.CompletedTask;
            }),
            new BulkUpdateObservationNotesApplier(items =>
            {
                foreach (var payload in items) ApplyNote(noteStore, payload);
                return Task.CompletedTask;
            }),

            new LaunchSessionApplier(p => sessions.LaunchSessionAsync(new SessionLaunchParams
            {
                Type = p.Type, Image = p.Image, Name = SessionName(p.Name, p.Type),
                Cores = p.Cores ?? 2, Ram = p.Ram ?? 8, Gpus = p.Gpus ?? 0,
            })),
            new LaunchHeadlessApplier(p => sessions.LaunchHeadlessAsync(new SessionLaunchParams
            {
                Type = "headless", Image = p.Image, Name = SessionName(p.Name, "headless"),
                Cores = p.Cores ?? 2, Ram = p.Ram ?? 8, Gpus = p.Gpus ?? 0,
                Args = p.Args, Replicas = p.Replicas ?? 1,
            })),
            new DeleteSessionApplier(p => sessions.DeleteSessionAsync(p.Id)),
            new RenewSessionApplier(p => sessions.RenewSessionAsync(p.Id)),

            new DownloadObservationApplier(p => DownloadObservationAsync(dataLink, observations, p.PublisherId)),
            new DeleteDownloadedObservationApplier(p =>
            {
                var match = observations.Observations.FirstOrDefault(o => o.Id == p.Id || o.PublisherID == p.Id);
                if (match is not null) observations.Remove(match);
                return Task.CompletedTask;
            }),

            new UploadTextToVoSpaceApplier(p =>
                storage.UploadFileAsync(p.Path, new MemoryStream(Encoding.UTF8.GetBytes(p.Content)), p.ContentType ?? "text/plain")),
            new CreateVoSpaceFolderApplier(p => storage.CreateFolderAsync(p.Path, p.Name)),
            new DeleteVoSpaceNodeApplier(p => storage.DeleteNodeAsync(p.Path)),
        };
    }

    /// <summary>Resolve an observation's FITS URL, stream it to ~/Downloads/Verbinal, and register it in Research.</summary>
    private static async Task DownloadObservationAsync(DataLinkService dataLink, ObservationStore store, string publisherId)
    {
        var links = await dataLink.GetLinksAsync(publisherId);
        var url = links.DirectFileUrl ?? dataLink.GetDownloadUrl(publisherId);

        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Verbinal");
        Directory.CreateDirectory(dir);
        var localPath = Path.Combine(dir, SafeFileName(publisherId) + ".fits");
        var tmp = localPath + ".tmp";

        using (var response = await dataLink.DownloadAsync(url, timeoutSeconds: 300))
        {
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            await using var fs = new FileStream(tmp, FileMode.Create);
            await stream.CopyToAsync(fs);
        }
        if (File.Exists(localPath)) File.Delete(localPath);
        File.Move(tmp, localPath);

        var observation = new DownloadedObservation { PublisherID = publisherId, LocalPath = localPath };
        var info = new FileInfo(localPath);
        if (info.Exists) observation.FileSize = info.Length;
        store.Save(observation);
    }

    private static string SafeFileName(string publisherId)
    {
        var name = publisherId;
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Length > 80 ? name[^80..] : name;
    }

    /// <summary>Probe the upstream services concurrently: any HTTP response = host reachable.</summary>
    private static async Task<IReadOnlyList<ServiceHealthEntry>> ProbeServicesAsync(IHttpClientFactory factory, ApiEndpoints endpoints)
    {
        var targets = new[]
        {
            ("CADC TAP (search)", endpoints.TapBaseUrl),
            ("Skaha (sessions)", endpoints.SkahaBaseUrl),
            ("ARC/VOSpace (storage)", endpoints.StorageBaseUrl),
            ("CADC auth", endpoints.LoginBaseUrl),
        };
        return await Task.WhenAll(targets.Select(t => ProbeOneAsync(factory, t.Item1, t.Item2)));
    }

    private static async Task<ServiceHealthEntry> ProbeOneAsync(IHttpClientFactory factory, string name, string url)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var client = factory.CreateClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            sw.Stop();
            return new ServiceHealthEntry(name, url, Reachable: true, (int)response.StatusCode, sw.ElapsedMilliseconds, null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ServiceHealthEntry(name, url, Reachable: false, null, sw.ElapsedMilliseconds, ex.GetType().Name);
        }
    }

    /// <summary>A safe Skaha session name (lowercase, hyphenated) — generated when the agent omits one.</summary>
    private static string SessionName(string? name, string type)
        => string.IsNullOrWhiteSpace(name) ? $"{type}-agent-{Guid.NewGuid().ToString("N")[..6]}" : name.Trim();

    private static void ApplyNote(ObservationNoteStore store, UpdateObservationNotePayload payload)
        => store.Upsert(ObservationNoteMerge.Apply(store.Get(payload.PublisherId), payload, DateTimeOffset.UtcNow));

    /// <summary>Open a local FITS file and parse its per-HDU headers (the static parser is stream-based).</summary>
    private static Task<List<FitsHeader>> ParseFitsHeadersAsync(string localPath)
        => Task.Run(() =>
        {
            // Unwrap a tar/gzip container the same way the FITS viewer does, so get_fits_header /
            // get_fits_wcs work on CADC's tar-bundled downloads too.
            using var stream = FitsContainer.OpenFits(localPath);
            return FitsParser.ParseHeaders(stream);
        });

    /// <summary>Resolve a CADC observation's preview images from DataLink (direct image files + #preview URLs).</summary>
    private static async Task<IReadOnlyList<PreviewArtifact>> ResolvePreviewImagesAsync(DataLinkService dataLink, string publisherId, CancellationToken ct)
    {
        var links = await dataLink.GetLinksAsync(publisherId, ct);
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
    private static async Task<PreviewBytes> FetchPreviewAsync(IHttpClientFactory factory, Uri url, int maxBytes, CancellationToken ct)
    {
        // The named client carries AuthTokenHandler (attaches the CADC token only to allow-listed hosts);
        // the redirect to signed minoc storage is pre-signed and needs no token.
        var client = factory.CreateClient("McpPreviewFetch");

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
