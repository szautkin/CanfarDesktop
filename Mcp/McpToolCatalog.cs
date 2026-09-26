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
        var tapSchema = sp.GetRequiredService<ITapSchemaService>();
        var annotations = sp.GetRequiredService<IAnnotationStore>();
        var jobs = sp.GetRequiredService<Tools.Proposals.JobRegistry>();
        var userImages = sp.GetRequiredService<IUserImageStore>();
        var registry = sp.GetRequiredService<IRegistryService>();
        var discoverySettings = sp.GetRequiredService<ImageDiscoverySettingsService>();
        var sessions = sp.GetRequiredService<ISessionService>();
        var imageCatalog = sp.GetRequiredService<IImageService>();
        var recentLaunches = sp.GetRequiredService<IRecentLaunchService>();
        var discovery = sp.GetRequiredService<ImageDiscoveryCoordinator>();
        var caom2 = sp.GetRequiredService<ICAOM2Service>();
        var dataLink = sp.GetRequiredService<DataLinkService>();
        var searchContext = sp.GetRequiredService<SearchContext>();
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
        // VizieR is public (no auth) — a plain client is deliberate. The mirror list is read live from
        // settings rather than captured, so editing it takes effect on the next search, not the next run.
        var appSettings = sp.GetRequiredService<ISettingsService>();
        var vizier = new VizierService(
            httpFactory.CreateClient(),
            () => VizierService.ParseEndpointList(appSettings.VizierMirrors));

        // Built once and exposed under BOTH the Windows name and its macOS alias (G5 wire parity).
        var uploadFileToVoSpace = new UploadFileToVoSpaceTool();
        var downloadVoSpaceFile = new DownloadVoSpaceFileTool((path, ct) => storage.DownloadFileAsync(path, ct));
        var createVoSpaceFolder = new CreateVoSpaceFolderTool();

        // Remote compute is the person's CANFAR account at work, and its tools are locked as its screen
        // is until they sign in — the history and state are answered from this machine, where nothing on
        // the platform would turn a signed-out caller away (see SignedInTool).
        IMcpTool Compute(IMcpTool tool) => new SignedInTool(tool, () => auth.IsAuthenticated,
            "Remote compute runs on the person's CANFAR account, so they need to sign in first. The Remote " +
            "Compute screen stays locked until they do, like Portal and Storage.");

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

            // The service's own schema, and the check that reads it. Both fetch on first use and share
            // one cached copy (TapSchemaService is a singleton).
            new DescribeTapSchemaTool(ct => tapSchema.GetSchemaAsync(ct)),
            new ValidateAdqlQueryTool(ct => tapSchema.GetSchemaAsync(ct)),
            new VizierConeSearchTool((req, ct) => vizier.ConeSearchAsync(
                req.Catalogue, req.RaDeg, req.DecDeg, req.RadiusDeg, req.RaColumn, req.DecColumn, req.MaxRec,
                req.Columns, ct)),

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

            // The other door: images the platform does not list, and what is inside the ones it does.
            // The same credentials the discovery settings already mint for x-skaha-registry-auth: someone
            // who configured discovery has configured this too, and nobody is asked for a secret twice.
            new SearchImageRegistryTool((query, ct) => registry.SearchAsync(
                discoverySettings.Settings.RegistryHost, query,
                new RegistryAuth(discoverySettings.CurrentAuthHeader()), ct)),
            new ListMyImagesTool(() => userImages.All()),
            new SearchPackagesTool(() => discovery.AllPackages()),
            new DescribeImageTool(id => discovery.DiscoveredManifests()
                .FirstOrDefault(m => string.Equals(m.ImageID, id, StringComparison.OrdinalIgnoreCase))),
            new AddRegistryImageTool(),
            new RemoveRegistryImageTool(),

            // AI Compute (Feature B): run agent code on a warm contributed session via the /arc file-drop.
            // run_code/start_compute are SemanticWrite (macOS parity — CANFAR compute is platform UX, not
            // billed usage), so they auto-apply under the user's auto-apply setting; stop_compute stays
            // Destructive (tears down a session mid-work). Disabled until an AI compute image is set in
            // Settings ▸ AI compute.
            Compute(new RunCodeTool(() => aiComputeSettings.Settings)),
            Compute(new RunCodeOutputTool((id, ct) => aiCompute.FetchOutAsync(id, ct))),
            Compute(new StartComputeTool(() => aiComputeSettings.Settings)),
            Compute(new StopComputeTool()),
            Compute(new GetComputeStateTool(async ct => ComputeStateView.From(await aiCompute.SnapshotAsync(ct), DateTimeOffset.UtcNow))),
            Compute(new ListComputeRunsTool(() => aiCompute.Runs.All())),

            // CAOM2 metadata + DataLink (download/preview URLs)
            new GetObservationCaom2Tool((id, ct) => caom2.GetByPublisherIdAsync(id, ct)),
            new GetDataLinksTool((id, ct) => dataLink.GetLinksAsync(id, ct)),
            // What each file can be cut by, and the cutout the editor would open on — SODA's descriptors
            // from DataLink, the last search for the suggestion.
            new GetCutoutOptionsTool(async (id, ct) =>
                CutoutOptions.From(id, await CutoutSourcesAsync(dataLink, caom2, observations, id, ct), searchContext.CutoutHints)),

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
            new GetServiceHealthTool(() => ProbeServicesAsync(httpFactory, endpoints), () => auth.IsAuthenticated),

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

            // Work that outlives the call that asked for it.
            new GetJobStatusTool(id => id is null
                ? jobs.All()
                : jobs.Get(id) is { } one ? [one] : []),
            new WithdrawProposalTool(),

            // Live ViewState writes: steer the user's view (no proposal)
            new NavigateToTool(mode => viewState.NavigateAsync(mode)),
            new SetSearchFocusTool((ra, dec) => viewState.SetSearchFocusActionAsync(ra, dec)),
            new OpenFitsFileTool(id => viewState.OpenFitsAsync(id)),

            // Search page: the form, the facets, the query, and the results grid the user is looking at.
            // search_observations (above) stays the headless way to run a query; these are for when the
            // point is that the USER ends up seeing it.
            new GetSearchFormTool(() => viewState.GetSearchFormAsync()),
            new SetSearchFormTool(patch => viewState.SetSearchFormAsync(patch)),
            new GetSearchConstraintsTool(() => viewState.GetSearchConstraintsAsync()),
            new SetSearchConstraintsTool(patch => viewState.SetSearchConstraintsAsync(patch)),
            new RunSearchTool(() => viewState.RunSearchAsync()),
            new SetAdqlQueryTool((adql, execute) => viewState.SetAdqlQueryAsync(adql, execute)),
            new ExecuteAdqlQueryTool(adql => viewState.ExecuteAdqlQueryAsync(adql)),
            new RunSavedQueryTool(name => viewState.RunSavedQueryAsync(name)),
            new GetSearchResultsTool(query => viewState.GetSearchResultsAsync(query)),
            new SetSearchResultsViewTool(patch => viewState.SetSearchResultsViewAsync(patch)),
            new ExportSearchResultsTool((format, path) => viewState.ExportSearchResultsAsync(format, path)),
            new ShowSearchRowDetailTool(row => viewState.ShowSearchRowDetailAsync(row)),
            new ShowObservationDetailTool(id => viewState.ShowObservationDetailAsync(id)),
            new ShowCutoutEditorTool(args => viewState.ShowCutoutEditorAsync(args)),

            // The Remote Compute screen and Storage-at-a-folder: what a person can do there, an agent
            // can show them — a run, code ready to run, the exec folder. Showing asks a signed-out person
            // to sign in, as navigate_to does; reading what the screen shows does not, since it cannot
            // be showing anything.
            new ShowComputeRunTool(id => viewState.ShowComputeRunAsync(id)),
            new SetComputeSnippetTool(r => viewState.SetComputeSnippetAsync(r)),
            Compute(new GetComputeViewTool(() => viewState.GetComputeViewAsync())),
            new ShowStorageFolderTool(folder => viewState.ShowStorageFolderAsync(folder)),
            new LoadRecentSearchTool(match => viewState.LoadRecentSearchAsync(match)),

            // The Search page's "Remove from history" and "Clear All". Written, tested and given
            // appliers, then never put on the server — so the two buttons had no agent equivalent.
            new RemoveRecentSearchTool(() => searchStore.LoadRecentSearches()),
            new ClearRecentSearchesTool(() => searchStore.LoadRecentSearches()),
            new ResetSearchFormTool(() => viewState.ResetSearchFormAsync()),

            // Marks on an image or a cube. The store is what makes them persist with the FILE, so these
            // work on a target that is not currently open — which is how a batch is prepared before
            // anyone looks at it.
            new AnnotateFitsTool(annotations, viewState),
            new AnnotateCubeTool(annotations, viewState),
            new ListFitsAnnotationsTool(annotations, viewState),
            new ListCubeAnnotationsTool(annotations, viewState),
            new UpdateAnnotationTool(annotations, viewState),
            new RemoveAnnotationTool(annotations, viewState),
            new ClearAnnotationsTool(annotations, viewState),

            new SelectAnnotationTool(annotations, viewState),

            // The figure the marks are drawn for.
            new ExportFitsFigureTool(request => viewState.ExportFitsFigureAsync(request)),
            new ExportAnnotationsTool(request => viewState.ExportAnnotationsAsync(request)),

            // Pointing a person at a control, and the vocabulary for doing it.
            new PointAtUiTool(request => viewState.PointAtUiAsync(request)),
            new ListUiTargetsTool((contains, collapsed) => viewState.ListUiTargetsAsync(contains, collapsed)),
            // Settings, opened at a section so the pointer can guide the person through it — never set.
            new OpenSettingsTool(section => viewState.OpenSettingsAsync(section)),
            new CloseSettingsTool(() => viewState.CloseSettingsAsync()),

            // Looking at what the person is looking at, as opposed to writing a plate for a paper.
            new GetFitsImageTool(request => viewState.CaptureFitsAsync(request)),
            new GetCubeImageTool(request => viewState.CaptureCubeAsync(request)),

            // 3D Cube Viewer: open + steer + read + probe + export figure
            // 3D Cube Viewer: open + steer + read + probe + export figure + transfer curve + tabs/recents
            new OpenCubeTool(target => viewState.OpenCubeAsync(target)),
            new SetCubeViewTool(args => viewState.SetCubeAsync(args)),
            new GetCubeViewTool(() => viewState.GetCubeAsync()),
            new ProbeCubeSpectrumTool((x, y) => viewState.ProbeCubeAsync(x, y)),
            new ShowCubeSpectrumTool((x, y) => viewState.ShowCubeSpectrumAsync(x, y), () => viewState.CloseCubeSpectrumAsync()),
            new SetCubeTransferTool((points, reset) => viewState.SetCubeTransferAsync(points, reset)),
            new GetCubeChannelProfileTool(() => viewState.GetCubeChannelProfileAsync()),
            new SwitchCubeTabTool(index => viewState.SwitchCubeTabAsync(index)),
            new ListRecentCubesTool(() => viewState.ListRecentCubesAsync()),
            new ExportCubeFigureTool(req => viewState.ExportCubeAsync(req)),

            // 2D FITS Viewer: steer + read + probe pixel + go-to coordinate + blink + tabs (active tab)
            new SetFitsViewTool(args => viewState.SetFitsAsync(args)),
            new GetFitsViewTool(() => viewState.GetFitsAsync()),
            new ProbeFitsPixelTool((x, y) => viewState.ProbeFitsAsync(x, y)),
            new FitsGotoCoordinateTool((ra, dec) => viewState.GotoFitsAsync(ra, dec)),
            new BlinkFitsTabsTool((action, tab, interval) => viewState.BlinkFitsAsync(action, tab, interval)),
            new SwitchFitsTabTool(index => viewState.SwitchFitsTabAsync(index)),
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
            new GetCellImageTool((i, nb) => viewState.GetCellImageAsync(i, nb)),
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

            // Reaching a tab that is not the active one — every other viewer tool acts on the active
            // one, so without these a second open file was unreachable.
            new CloseTabTool((kind, index) => viewState.CloseTabAtAsync(kind, index)),

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
            // Part of a file, cut on CADC's side — checked against the file's descriptor before it is queued.
            new DownloadCutoutTool((id, ct) => CutoutSourcesAsync(dataLink, caom2, observations, id, ct)),
            new DeleteDownloadedObservationTool(),
            new ClearResearchArchiveTool(),
            // Keep an observation without its file; drop a file and keep its observation.
            new SaveObservationTool(),
            new RemoveDownloadedFileTool(),
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

        // The map, added last and reading the finished catalogue — including itself. The closures hold
        // the list rather than a copy of it, so a tool added above this line is one they can find; a
        // snapshot taken here would go stale the moment anything else was appended.
        tools.Add(new ListAppsTool(() => tools.Select(t => t.Descriptor.Name).ToList()));
        tools.Add(new SearchToolsTool(() => tools.Select(t => t.Descriptor).ToList()));
        tools.Add(new ManTool(() => tools.Select(t => t.Descriptor).ToList()));

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
        var downloader = sp.GetRequiredService<ObservationDownloader>();
        var dataLink = sp.GetRequiredService<DataLinkService>();
        var storage = sp.GetRequiredService<IStorageService>();
        var discovery = sp.GetRequiredService<ImageDiscoveryCoordinator>();
        var aiGuide = sp.GetRequiredService<AiGuideService>();
        var caom2 = sp.GetRequiredService<ICAOM2Service>();
        var aiCompute = sp.GetRequiredService<CanfarDesktop.Services.AICompute.AIComputeService>();
        var userImages = sp.GetRequiredService<IUserImageStore>();

        return new IProposalApplier[]
        {
            new AddRegistryImageApplier(payload =>
            {
                userImages.Add(Models.RegistryImage.FromLabels(payload.ImageID, payload.Types));
                return Task.CompletedTask;
            }),
            new RemoveRegistryImageApplier(payload =>
            {
                userImages.Remove(payload.ImageID);
                return Task.CompletedTask;
            }),
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
            new RemoveRecentSearchApplier(payload =>
            {
                // Keyed by (searchedAt, summary), not index, so the right entry is removed even if the
                // history shifted between the proposal and the user's approval.
                var remaining = searchStore.LoadRecentSearches()
                    .Where(s => s.SearchedAt != payload.SearchedAt || !string.Equals(s.Summary, payload.Summary, StringComparison.Ordinal))
                    .ToList();
                searchStore.SaveAllRecentSearches(remaining);
                return Task.CompletedTask;
            }),
            new ClearRecentSearchesApplier(() =>
            {
                searchStore.ClearRecentSearches();
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
                DownloadObservationAsync(downloader, caom2, p.PublisherId, p.ArtifactIndex, attribution)),
            new DownloadObservationsBulkApplier((p, attribution) =>
                DownloadObservationAsync(downloader, caom2, p.PublisherId, p.ArtifactIndex, attribution)),
            new DownloadCutoutApplier((p, attribution) => DownloadCutoutAsync(downloader, dataLink, caom2, p, attribution)),
            new SaveObservationApplier(async (p, attribution) =>
                observations.SaveIfAbsent(await AgentRecordAsync(caom2, p.PublisherId, attribution,
                    await dataLink.GetLinksAsync(p.PublisherId)))),
            new RemoveDownloadedFileApplier(p =>
            {
                var match = observations.Find(p.Id)
                    ?? throw new InvalidOperationException($"'{p.Id}' is not in Research — list_downloaded_observations shows what is");
                return ResearchRecords.RemoveLocalFile(observations, match) is { } why
                    ? throw new InvalidOperationException(why)
                    : Task.CompletedTask;
            }),
            new DeleteDownloadedObservationApplier(p =>
            {
                var match = observations.Find(p.Id);
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
            new RunCodeApplier(req => aiCompute.SubmitAsync(req, CanfarDesktop.Models.AICompute.ComputeRunAuthor.Agent)),
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

    /// <summary>
    /// Download an observation to ~/Downloads/Verbinal and register it in Research — through the same
    /// app-owned downloader a person's download uses, so an agent's shows in the status bar too.
    ///
    /// <para>Awaited, since the apply reports its outcome; not bounded by McpHost's apply backstop, and
    /// deliberately so: a MegaPipe tile is 1.6 GB and takes minutes, which the backstop hands on to a
    /// background job rather than calling a failure. A DEAD transfer is stopped by the downloader's
    /// stall timeout instead.</para>
    /// </summary>
    private static async Task DownloadObservationAsync(
        ObservationDownloader downloader, ICAOM2Service caom2,
        string publisherId, int? artifactIndex, AgentAttribution? attribution = null)
    {
        var localPath = Path.Combine(AgentDownloadsFolder(), SafeFileName(publisherId) + ".fits");
        var observation = await AgentRecordAsync(caom2, publisherId, attribution);
        await downloader.Start(new ObservationDownloadRequest(publisherId, localPath, observation, ArtifactIndex: artifactIndex));
    }

    /// <summary>Where an agent's downloads land: ~/Downloads/Verbinal, made on first use.</summary>
    private static string AgentDownloadsFolder()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Verbinal");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Apply download_cutout. The file's descriptor is fetched again — a proposal can wait a while —
    /// the request is built from the spec that passed the check when it was proposed, and the download is
    /// handed to the app like any other, recorded in Research as a CUTOUT of the observation.
    /// </summary>
    private static async Task DownloadCutoutAsync(
        ObservationDownloader downloader, DataLinkService dataLink, ICAOM2Service caom2,
        DownloadCutoutPayload payload, AgentAttribution? attribution)
    {
        // The downloader resolves the SODA request from the record's own cutout, against today's descriptor.
        var links = await dataLink.GetLinksAsync(payload.PublisherId);
        var localPath = Path.Combine(AgentDownloadsFolder(), SafeFileName(payload.Spec.FileName));
        var record = await AgentRecordAsync(caom2, payload.PublisherId, attribution, links, payload.Spec);
        await downloader.Start(new ObservationDownloadRequest(payload.PublisherId, localPath, record));
    }

    /// <summary>
    /// The ways an observation's files can be cut, for get_cutout_options and download_cutout alike:
    /// CADC's from DataLink, each file's size from CAOM2, and the observation's file on this computer.
    /// </summary>
    private static async Task<IReadOnlyList<Services.Cutouts.ICutoutSource>> CutoutSourcesAsync(
        DataLinkService dataLink, ICAOM2Service caom2, ObservationStore observations, string publisherId, CancellationToken ct)
    {
        var links = await dataLink.GetLinksAsync(publisherId, ct);

        CAOM2Observation? observation = null;
        try
        {
            var meta = await caom2.GetByPublisherIdAsync(publisherId, ct);
            if (meta.IsSuccess) observation = meta.Observation;
        }
        catch { /* the sizes are a help, not a requirement */ }

        var local = await Task.Run(() => Services.Cutouts.CutoutSources.Local(
            observations.Observations, publisherId, Services.Cutouts.CutoutSources.ArtifactIds(observation)), ct);
        return [.. Services.Cutouts.CutoutSources.Soda(links, observation), .. local is null ? [] : new[] { local }];
    }

    /// <summary>
    /// The Research record for something an agent fetches, with CAOM2's metadata so it is not bare (the
    /// UI fills these from the search row; an agent has none). Best-effort and bounded, and done before
    /// the download starts so the record is whole: a metadata failure (embargo, timeout, parse) must
    /// not stop the download.
    /// </summary>
    private static async Task<DownloadedObservation> AgentRecordAsync(
        ICAOM2Service caom2, string publisherId, AgentAttribution? attribution,
        DataLinkResult? links = null, Models.Cutouts.CutoutSpec? cutout = null)
    {
        CAOM2Observation? meta = null;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var result = await caom2.GetByPublisherIdAsync(publisherId, cts.Token);
            if (result.IsSuccess) meta = result.Observation;
        }
        catch { /* the record without the metadata rather than no download at all */ }

        return ResearchRecords.ForObservation(publisherId, meta, links, cutout, attribution);
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

    /// <summary>Probe the upstream services concurrently by reading each one's IVOA availability document.</summary>
    private static async Task<IReadOnlyList<ServiceHealthEntry>> ProbeServicesAsync(IHttpClientFactory factory, ApiEndpoints endpoints)
        => (await CanfarDesktop.Services.ServiceHealthProbe.ProbeCoreAsync(factory, endpoints))
            .Select(r => new ServiceHealthEntry(
                r.Name, r.Url, r.Reachable, r.Ok, r.StatusCode, r.LatencyMs, r.Error,
                r.Available, r.Note, r.RequiresAuth))
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
