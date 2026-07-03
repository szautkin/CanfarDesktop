using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Models.Caom2;
using CanfarDesktop.Models.Fits;
using CanfarDesktop.Services;
using CanfarDesktop.Services.AiGuide;
using CanfarDesktop.Services.Database;
using CanfarDesktop.Services.Export;
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
/// running DI graph). The set spans read-only (AgentSafe) reads, live ViewState mutators (cube / FITS /
/// notebook steering + figure/bundle exports), and proposal-based SemanticWrite/Destructive writes.
/// Per-tool agent gating lives on each tool's verb class, not here.
/// </summary>
public static class McpToolCatalog
{
    public static IReadOnlyList<IMcpTool> Build(IServiceProvider sp, string appVersion, AgentEventLog? eventLog = null)
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
        var aiGuide = sp.GetRequiredService<AiGuideService>();
        var aiComputeSettings = sp.GetRequiredService<CanfarDesktop.Services.AICompute.AIComputeSettingsService>();
        var aiCompute = sp.GetRequiredService<CanfarDesktop.Services.AICompute.AIComputeService>();
        var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
        var previewFetcher = new McpPreviewFetcher(dataLink, httpFactory);
        // VizieR is public (no auth) — a plain client is deliberate.
        var vizier = new VizierService(httpFactory.CreateClient());

        // Built once and exposed under BOTH the Windows name and its macOS alias (G5 wire parity).
        var uploadFileToVoSpace = new UploadFileToVoSpaceTool();
        var downloadVoSpaceFile = new DownloadVoSpaceFileTool((path, ct) => storage.DownloadFileAsync(path, ct));
        var createVoSpaceFolder = new CreateVoSpaceFolderTool();

        var tools = new List<IMcpTool>
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
            new VizierConeSearchTool((req, ct) => vizier.ConeSearchAsync(
                req.Catalogue, req.RaDeg, req.DecDeg, req.RadiusDeg, req.RaColumn, req.DecColumn, req.MaxRec, ct)),

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
            new FindImagesWithPackagesTool(
                query => discovery.Search(query),
                async ct =>
                {
                    try
                    {
                        var raw = await imageCatalog.GetImagesAsync(ct);
                        return (IReadOnlyList<CatalogueImage>)raw.Select(i => new CatalogueImage(i.Id, i.Types)).ToList();
                    }
                    catch
                    {
                        // Catalogue endpoint flaky → synthesize from probed manifests (loses types, so
                        // type-filtered queries match nothing) rather than failing the call. Mirrors macOS.
                        return discovery.KnownImages().Select(id => new CatalogueImage(id, Array.Empty<string>())).ToList();
                    }
                },
                () => discovery.KnownImages(),
                (query, minScore, limit) => discovery.SearchPartial(query, minScore, limit)),
            // discover_image_packages (write) — probe an image so find_images_with_packages can match it.
            new DiscoverImagePackagesTool(),

            // AI Compute (Feature B): run agent code on a warm contributed session via the /arc file-drop.
            // run_code/start_compute are SemanticWrite (macOS parity — CANFAR compute is platform UX, not
            // billed usage), so they auto-apply under the user's auto-apply setting; stop_compute stays
            // Destructive (tears down a session mid-work). Disabled until an AI compute image is set in
            // Settings ▸ AI compute.
            new RunCodeTool(() => aiComputeSettings.Settings),
            new RunCodeOutputTool((id, ct) => aiCompute.FetchOutAsync(id, ct)),
            new StartComputeTool(() => aiComputeSettings.Settings),
            new StopComputeTool(),

            // CAOM2 metadata + DataLink (download/preview URLs)
            new GetObservationCaom2Tool((id, ct) => caom2.GetByPublisherIdAsync(id, ct)),
            new GetDataLinksTool((id, ct) => dataLink.GetLinksAsync(id, ct)),

            // VOSpace/ARC storage (read) + local FITS introspection
            new ListVoSpacePathTool((req, ct) => storage.ListNodesAsync(req.Path, req.Limit, ct)),
            new GetVoSpaceNodeTool((req, ct) => storage.ListNodesAsync(req.Path, req.Limit, ct)),
            new ReadVoSpaceFileTool((path, ct) => storage.DownloadFileAsync(path, ct)),
            downloadVoSpaceFile,
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
            new GetPreviewImageTool(previewFetcher.ResolveAsync, previewFetcher.FetchAsync),

            // Proposal lifecycle: let the agent see + manage its queued write proposals
            new ListPendingProposalsTool(),
            new GetProposalStateTool(),
            new WithdrawProposalTool(),

            // Live ViewState writes: steer the user's view (no proposal)
            new NavigateToTool(mode => viewState.NavigateAsync(mode)),
            new SetSearchFocusTool((ra, dec) => viewState.SetSearchFocusActionAsync(ra, dec)),
            new OpenFitsFileTool(id => viewState.OpenFitsAsync(id)),

            // 3D Cube Viewer: open + steer + read + probe + export figure
            new OpenCubeTool(target => viewState.OpenCubeAsync(target)),
            new SetCubeViewTool(args => viewState.SetCubeAsync(args)),
            new GetCubeViewTool(() => viewState.GetCubeAsync()),
            new ProbeCubeSpectrumTool((x, y) => viewState.ProbeCubeAsync(x, y)),
            new ExportCubeFigureTool((path, format, scale, dark) => viewState.ExportCubeAsync(path, format, scale, dark)),

            // 2D FITS Viewer: steer + read + probe pixel + go-to coordinate (active tab)
            new SetFitsViewTool(args => viewState.SetFitsAsync(args)),
            new GetFitsViewTool(() => viewState.GetFitsAsync()),
            new ProbeFitsPixelTool((x, y) => viewState.ProbeFitsAsync(x, y)),
            new FitsGotoCoordinateTool((ra, dec) => viewState.GotoFitsAsync(ra, dec)),
            // FITS coordinate bookmarks (persisted saved coordinates)
            new ListFitsBookmarksTool(() => viewState.ListFitsBookmarksAsync()),
            new SaveFitsBookmarkTool((ra, dec, label, src) => viewState.SaveFitsBookmarkAsync(ra, dec, label, src)),
            new DeleteFitsBookmarkTool(id => viewState.DeleteFitsBookmarkAsync(id)),

            // Native notebook editor: read + lifecycle + cell CRUD + kernel/execution (active tab)
            // Workflows: research protocols the agent reads, follows, authors, and checks off.
            new ListWorkflowsTool(sp.GetRequiredService<CanfarDesktop.Services.Workflows.WorkflowStore>()),
            new GetWorkflowTool(sp.GetRequiredService<CanfarDesktop.Services.Workflows.WorkflowStore>()),
            new SaveWorkflowTool(),
            new UpdateWorkflowTool(),
            new SetWorkflowStepTool(),
            new UseWorkflowTool(),
            new DeleteWorkflowTool(),

            new ListNotebooksTool(() => viewState.ListNotebooksAsync()),
            new ListOpenNotebooksTool(() => viewState.ListOpenNotebooksAsync()),
            new GetNotebookTool(nb => viewState.GetNotebookAsync(nb)),
            new GetCellOutputTool((i, nb) => viewState.GetCellOutputAsync(i, nb)),
            new GetKernelStateTool(nb => viewState.GetKernelStateAsync(nb)),
            new OpenNotebookTool(cmd => viewState.NotebookMutateAsync(cmd)),
            new CreateNotebookTool(cmd => viewState.NotebookMutateAsync(cmd)),
            new SaveNotebookTool(cmd => viewState.NotebookMutateAsync(cmd)),
            new EditCellTool(cmd => viewState.NotebookMutateAsync(cmd)),
            new AddCellTool(cmd => viewState.NotebookMutateAsync(cmd)),
            new DeleteCellTool(cmd => viewState.NotebookMutateAsync(cmd)),
            new ChangeCellTypeTool(cmd => viewState.NotebookMutateAsync(cmd)),
            new MoveCellTool(cmd => viewState.NotebookMutateAsync(cmd)),
            new RunCellTool(cmd => viewState.NotebookMutateAsync(cmd)),
            new RunAllCellsTool(cmd => viewState.NotebookMutateAsync(cmd)),
            new ClearCellOutputsTool(cmd => viewState.NotebookMutateAsync(cmd)),
            new StartKernelTool(cmd => viewState.NotebookMutateAsync(cmd)),
            new InterruptKernelTool(cmd => viewState.NotebookMutateAsync(cmd)),
            new RestartKernelTool(cmd => viewState.NotebookMutateAsync(cmd)),
            // Search → notebook hand-off: open a pre-seeded analysis notebook for a downloaded observation.
            new CreateAnalysisNotebookTool((id, tmpl) => viewState.CreateAnalysisNotebookAsync(id, tmpl)),

            // Tab management: close the active viewer tab / count open tabs (open_* tools accumulate them)
            new CloseActiveTabTool(kind => viewState.CloseTabAsync(kind)),
            new ListOpenTabsTool(() => viewState.ListTabsAsync()),

            // Semantic writes (proposals; auto-apply or queue per the autonomy toggle)
            new SaveQueryTool(),
            new DeleteSavedQueryTool(),
            new UpdateObservationNoteTool(),
            new BulkUpdateObservationNotesTool(),

            // Skaha session lifecycle
            new LaunchSessionTool(),
            new LaunchHeadlessJobTool(),
            new DeleteSessionTool(),
            new DeleteSessionsBulkTool(),
            new RenewSessionTool(),

            // Research: download / remove observations + export a Claude-friendly bundle
            new DownloadObservationTool(),
            new DownloadObservationsBulkTool(),
            new DeleteDownloadedObservationTool(),
            new ClearResearchArchiveTool(),
            new ExportResearchBundleTool((dest, notes, hist, files, upload, ct) =>
                ExportResearchBundleAsync(sp, appVersion, dest, notes, hist, files, upload)),

            // VOSpace/ARC storage writes
            new UploadTextToVoSpaceTool(),
            uploadFileToVoSpace,
            createVoSpaceFolder,
            new SetVoSpaceAclTool(),
            new DeleteVoSpaceNodeTool(),
            new ClearUserSiteTool(),

            // macOS-name aliases (G5 wire parity): same schema/dispatch as the Windows tool they
            // wrap, described with the macOS wording so agents written against Verbinal-macOS work.
            new AliasedTool(
                "upload_to_vospace",
                "Upload a downloaded observation's local file to a VOSpace path. Use `upload_text_to_vospace` " +
                "instead if your source is in-conversation text (script, config, JSON) rather than a downloaded " +
                "file. Synchronous with a 150s applier deadline; a stuck transfer surfaces as `backendError` " +
                "with the deadline named, not a silent hang. For files > ~100 MB on slow links: the underlying " +
                "transfer can outlast the MCP transport timeout — on `Request timed out`, re-poll " +
                "`list_vospace_path` after 30–60s, the bytes are often there.",
                uploadFileToVoSpace),
            new AliasedTool(
                "download_from_vospace",
                "Download a VOSpace file to the user's Downloads folder. Synchronous with a 150s applier " +
                "deadline; a stuck transfer surfaces as `backendError` with the deadline named, not a silent " +
                "hang. For files > ~100 MB on slow links: the underlying transfer can outlast the MCP transport " +
                "timeout — on `Request timed out` re-check the Downloads folder before retrying, the bytes are " +
                "often there.",
                downloadVoSpaceFile),
            new AliasedTool(
                "vospace_mkdir",
                "Create a folder under a VOSpace path.",
                createVoSpaceFolder),

            // AI Guide management: let the agent re-tune its own tool surface — list/add/update/delete
            // guide tools + override/reset another tool's description (the MCP server reads these live).
            new ListGuideToolsTool(() => aiGuide.Snapshot().Guides),
            new SetToolDescriptionTool(),
            new ClearToolDescriptionTool(),
            new AddGuideToolTool(),
            new UpdateGuideToolTool(),
            new DeleteGuideToolTool(),
        };

        // list_events needs the host's proposal-lifecycle event buffer; hosts that don't wire one
        // (pure catalog consumers like AiGuideToolInventory) simply don't expose the tool.
        if (eventLog is not null)
            tools.Add(new ListEventsTool(eventLog));

        return tools;
    }

    /// <summary>Build the proposal appliers bound to the live stores (registered by the host).</summary>
    public static IReadOnlyList<IProposalApplier> BuildAppliers(IServiceProvider sp)
    {
        var auth = sp.GetRequiredService<IAuthService>();
        var searchStore = sp.GetRequiredService<ISearchStoreService>();
        var noteStore = sp.GetRequiredService<ObservationNoteStore>();
        var sessions = sp.GetRequiredService<ISessionService>();
        var observations = sp.GetRequiredService<ObservationStore>();
        var downloads = sp.GetRequiredService<ObservationDownloadService>();
        var storage = sp.GetRequiredService<IStorageService>();
        var discovery = sp.GetRequiredService<ImageDiscoveryCoordinator>();
        var aiGuide = sp.GetRequiredService<AiGuideService>();
        var caom2 = sp.GetRequiredService<ICAOM2Service>();
        var aiCompute = sp.GetRequiredService<CanfarDesktop.Services.AICompute.AIComputeService>();

        return new IProposalApplier[]
        {
            new SaveQueryApplier((payload, attribution) =>
            {
                searchStore.SaveQuery(new SavedQuery
                {
                    Name = payload.Name, Adql = payload.Adql, SavedAt = DateTime.UtcNow,
                    AgentAttribution = attribution,
                });
                return Task.CompletedTask;
            }),
            new DeleteSavedQueryApplier(payload =>
            {
                searchStore.DeleteQuery(payload.Name);
                return Task.CompletedTask;
            }),
            new UpdateObservationNoteApplier((payload, attribution) =>
            {
                ApplyNote(noteStore, payload, attribution);
                return Task.CompletedTask;
            }),
            new BulkUpdateObservationNotesApplier((items, attribution) =>
            {
                foreach (var payload in items) ApplyNote(noteStore, payload, attribution);
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
                Cmd = p.Cmd, Args = p.Args, Replicas = p.Replicas ?? 1,
            })),
            new DeleteSessionApplier(p => sessions.DeleteSessionAsync(p.Id)),
            new DeleteSessionsBulkApplier(id => sessions.DeleteSessionAsync(id)),
            new RenewSessionApplier(p => sessions.RenewSessionAsync(p.Id)),

            new DownloadObservationApplier((p, attribution) =>
                DownloadObservationAsync(downloads, observations, caom2, p.PublisherId, p.ArtifactIndex, attribution)),
            new DownloadObservationsBulkApplier((p, attribution) =>
                DownloadObservationAsync(downloads, observations, caom2, p.PublisherId, p.ArtifactIndex, attribution)),
            new DeleteDownloadedObservationApplier(p =>
            {
                var match = observations.Observations.FirstOrDefault(o => o.Id == p.Id || o.PublisherID == p.Id);
                if (match is not null) observations.Remove(match);
                return Task.CompletedTask;
            }),
            new ClearResearchArchiveApplier(
                () => observations.Observations,
                o => observations.Remove(o),
                publisherId => noteStore.Delete(publisherId),
                path => { if (File.Exists(path)) File.Delete(path); }),

            new UploadTextToVoSpaceApplier(p =>
                storage.UploadFileAsync(p.Path, new MemoryStream(Encoding.UTF8.GetBytes(p.Content)), p.ContentType ?? "text/plain")),
            new UploadFileToVoSpaceApplier(async p =>
            {
                await using var fs = new FileStream(p.LocalPath, FileMode.Open, FileAccess.Read);
                await storage.UploadFileAsync(p.VospacePath, fs, p.ContentType);
            }),
            new CreateVoSpaceFolderApplier(p => storage.CreateFolderAsync(p.Path, p.Name)),
            new SetVoSpaceAclApplier(p => storage.SetNodeAclAsync(p.Path, p.GroupRead, p.GroupWrite, p.IsPublic)),
            new DeleteVoSpaceNodeApplier(p => storage.DeleteNodeAsync(p.Path)),
            new ClearUserSiteApplier(
                () => auth.CurrentUsername,
                (path, ct) => storage.ListNodesAsync(path, null, ct),
                (path, ct) => storage.DeleteNodeAsync(path, ct)),

            // AI Compute: submit code / pre-warm / stop the contributed compute session.
            new RunCodeApplier(req => aiCompute.SubmitAsync(req)),
            new StartComputeApplier(() => aiCompute.EnsureSessionAsync()),
            new StopComputeApplier(() => aiCompute.StopAsync()),

            new DiscoverImagePackagesApplier(p => p.Force
                ? discovery.RediscoverAsync(p.Image)
                : discovery.DiscoverAsync(p.Image)),

            // Workflows: local writes go straight to the thread-safe store; a vospace save publishes
            // to vos:<user>/workflows/ via the same storage path the upload tools use.
            new SaveWorkflowApplier(sp.GetRequiredService<CanfarDesktop.Services.Workflows.WorkflowStore>(),
                async (fileName, text, ct) =>
                {
                    var user = (auth.CurrentUsername ?? string.Empty).Trim();
                    if (user.Length == 0) throw ProposalApplyException.BackendError("not authenticated — sign in to publish to VOSpace");
                    try { await storage.CreateFolderAsync(user, "workflows", ct); }
                    catch { /* folder probably exists — the upload below is the real test */ }
                    await storage.UploadFileAsync($"{user}/workflows/{fileName}",
                        new MemoryStream(Encoding.UTF8.GetBytes(text)), "text/markdown", ct);
                }),
            new UpdateWorkflowApplier(sp.GetRequiredService<CanfarDesktop.Services.Workflows.WorkflowStore>()),
            new SetWorkflowStepApplier(sp.GetRequiredService<CanfarDesktop.Services.Workflows.WorkflowStore>()),
            new UseWorkflowApplier(sp.GetRequiredService<CanfarDesktop.Services.Workflows.WorkflowStore>()),
            new DeleteWorkflowApplier(sp.GetRequiredService<CanfarDesktop.Services.Workflows.WorkflowStore>()),

            // AI Guide management — re-tune the agent's own tool surface via the live AiGuideService.
            new SetToolDescriptionApplier(p => { aiGuide.SetOverride(p.ToolName, p.Description); return Task.CompletedTask; }),
            new ClearToolDescriptionApplier(p => { aiGuide.ClearOverride(p.ToolName); return Task.CompletedTask; }),
            new AddGuideToolApplier(p => { aiGuide.AddGuide(p.Name, p.Description, p.Body); return Task.CompletedTask; }),
            new UpdateGuideToolApplier(p => { aiGuide.UpdateGuide(Guid.Parse(p.Id), p.Name, p.Description, p.Body); return Task.CompletedTask; }),
            new DeleteGuideToolApplier(p => { aiGuide.DeleteGuide(Guid.Parse(p.Id)); return Task.CompletedTask; }),
        };
    }

    /// <summary>Resolve an observation's FITS URL, stream it to ~/Downloads/Verbinal, and register it in Research.</summary>
    private static async Task DownloadObservationAsync(
        ObservationDownloadService downloads, ObservationStore store, ICAOM2Service caom2,
        string publisherId, int? artifactIndex, AgentAttribution? attribution = null)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Verbinal");
        Directory.CreateDirectory(dir);
        var localPath = Path.Combine(dir, SafeFileName(publisherId) + ".fits");

        var url = await downloads.ResolveUrlAsync(publisherId, artifactIndex);
        // Bounded below McpHost's apply backstop so a stuck download fails with its own error (and releases
        // the apply gate) rather than tripping the generic apply timeout.
        await downloads.DownloadToPathAsync(url, localPath, timeoutSeconds: 120);

        var observation = new DownloadedObservation
        {
            PublisherID = publisherId, LocalPath = localPath, AgentAttribution = attribution,
        };
        var info = new FileInfo(localPath);
        if (info.Exists) observation.FileSize = info.Length;

        // Populate research metadata from CAOM2 so an agent-downloaded record isn't bare (the UI fills
        // these from the search row; the MCP path otherwise leaves collection/target/instrument empty).
        // Best-effort + bounded: a metadata failure (embargo/timeout/parse) must not lose the download.
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var meta = await caom2.GetByPublisherIdAsync(publisherId, cts.Token);
            if (meta.IsSuccess) PopulateFromCaom2(observation, meta.Observation);
        }
        catch { /* keep the downloaded file even if metadata is unavailable */ }

        store.Save(observation);
    }

    /// <summary>Fill the research-record metadata fields from a CAOM2 document (RA/Dec from the plane footprint centroid).</summary>
    private static void PopulateFromCaom2(DownloadedObservation obs, CAOM2Observation? caom2)
    {
        if (caom2 is null) return;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        obs.Collection = caom2.Collection;
        obs.ObservationID = caom2.ObservationID;
        obs.TargetName = caom2.Target?.Name ?? string.Empty;
        obs.Instrument = caom2.Instrument?.Name ?? string.Empty;
        if (caom2.Proposal is { } prop)
        {
            obs.ProposalId = prop.Id ?? string.Empty;
            obs.ProposalPi = prop.Pi ?? string.Empty;
            obs.ProposalTitle = prop.Title ?? string.Empty;
        }

        var plane = caom2.Planes.FirstOrDefault();
        if (plane is null) return;
        if (plane.CalibrationLevel is int cl) obs.CalLevel = cl.ToString(inv);
        if (plane.DataRelease is { } dr) obs.DataRelease = dr.ToString("yyyy-MM-dd", inv);
        if (plane.Position?.Polygon is { Count: > 0 } poly)
        {
            obs.RA = poly.Average(v => v.Ra).ToString("F6", inv);
            obs.Dec = poly.Average(v => v.Dec).ToString("F6", inv);
        }
    }

    /// <summary>Assemble + zip a research bundle from the registered export modules; optionally upload it to VOSpace.</summary>
    private static async Task<ExportBundleResult> ExportResearchBundleAsync(
        IServiceProvider sp, string appVersion, string destFolder,
        bool includeNotes, bool includeSearchHistory, bool includeFiles, bool uploadToVospace)
    {
        var modules = sp.GetServices<IExportableModule>().ToList();
        var options = new ExportOptions
        {
            IncludeNotes = includeNotes,
            IncludeSearchHistory = includeSearchHistory,
            IncludeFileCopies = includeFiles,
        };
        var svc = sp.GetRequiredService<ExportService>();
        var bundleDir = await svc.BuildBundleAsync(destFolder, modules, options, DateTimeOffset.Now, appVersion, Environment.MachineName);
        var zipPath = svc.ZipBundle(bundleDir);

        string? remote = null;
        if (uploadToVospace)
        {
            var auth = sp.GetRequiredService<IAuthService>();
            if (auth.CurrentUsername is { Length: > 0 } username)
            {
                var storage = sp.GetRequiredService<IStorageService>();
                // Pre-flight (SCI-12-1): don't begin a multi-GB upload that fails mid-way on quota — surface
                // a clear, upfront error. The local bundle is already written, so the agent isn't empty-handed.
                var quota = await storage.GetQuotaAsync(username);
                var zipSize = new FileInfo(zipPath).Length;
                if (quota is { QuotaBytes: > 0 } && quota.UsedBytes + zipSize > quota.QuotaBytes)
                    throw new McpToolException(new InvalidArgument(
                        $"VOSpace quota insufficient: {quota.UsedGB:F1} GB used of {quota.QuotaGB:F0} GB, and the bundle is " +
                        $"{zipSize / 1_073_741_824.0:F2} GB. Free space (or export with includeFiles:false). " +
                        $"The local bundle was still written to {destFolder}."));
                remote = await svc.UploadBundleToVoSpaceAsync(zipPath, storage, username);
            }
        }
        return new ExportBundleResult(bundleDir, zipPath, remote);
    }

    private static string SafeFileName(string publisherId)
    {
        var name = publisherId;
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Length > 80 ? name[^80..] : name;
    }

    /// <summary>Probe the upstream services concurrently: any HTTP response = host reachable.</summary>
    private static async Task<IReadOnlyList<ServiceHealthEntry>> ProbeServicesAsync(IHttpClientFactory factory, ApiEndpoints endpoints)
        => (await CanfarDesktop.Services.ServiceHealthProbe.ProbeCoreAsync(factory, endpoints))
            .Select(r => new ServiceHealthEntry(r.Name, r.Url, r.Reachable, r.StatusCode, r.LatencyMs, r.Error))
            .ToList();

    /// <summary>A safe Skaha session name (lowercase, hyphenated) — generated when the agent omits one.</summary>
    private static string SessionName(string? name, string type)
        => string.IsNullOrWhiteSpace(name) ? $"{type}-agent-{Guid.NewGuid().ToString("N")[..6]}" : name.Trim();

    private static void ApplyNote(ObservationNoteStore store, UpdateObservationNotePayload payload,
        AgentAttribution? attribution)
    {
        var merged = ObservationNoteMerge.Apply(store.Get(payload.PublisherId), payload, DateTimeOffset.UtcNow);
        store.Upsert(merged with { AgentAttribution = attribution ?? merged.AgentAttribution });
    }

    /// <summary>Open a local FITS file and parse its per-HDU headers (the static parser is stream-based).</summary>
    private static Task<List<FitsHeader>> ParseFitsHeadersAsync(string localPath)
        => Task.Run(() =>
        {
            // Unwrap a tar/gzip container the same way the FITS viewer does, so get_fits_header /
            // get_fits_wcs work on CADC's tar-bundled downloads too.
            using var stream = FitsContainer.OpenFits(localPath);
            return FitsParser.ParseHeaders(stream);
        });

}
