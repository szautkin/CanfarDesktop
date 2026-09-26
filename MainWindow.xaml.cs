using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Services;
using CanfarDesktop.Services.HttpClients;
using CanfarDesktop.Services.Notebook;
using CanfarDesktop.Helpers.Notebook;
using CanfarDesktop.Mcp;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.ViewModels;
using CanfarDesktop.Views;
using CanfarDesktop.Views.Dialogs;
using CanfarDesktop.Views.Notebook;
using static CanfarDesktop.Views.WindowHelper;

namespace CanfarDesktop;

public sealed partial class MainWindow : Window, CanfarDesktop.Mcp.Tools.Write.IAnnotationHost
{
    private enum AppMode { Landing, Portal, Search, Research, Storage, Notebook, FitsViewer, ObservationDetail, CubeViewer, AiGuide, Workflows, RemoteCompute }

    private readonly MainViewModel _viewModel;
    private readonly ILegalAgreementService _legal;
    private readonly LandingView _landingView;
    private DashboardPage? _dashboardPage;
    private SearchPage? _searchPage;
    private ResearchPage? _researchPage;
    private StorageBrowserPage? _storagePage;
    private ObservationDetailPage? _obsDetailPage;
    private AiGuidePage? _aiGuidePage;
    private Views.WorkflowsPage? _workflowsPage;
    private Views.RemoteComputePage? _remoteComputePage;
    private LocalFileBrowserPanel? _filePanel;
    private bool _filePanelVisible;
    private AppMode _currentMode = AppMode.Landing;
    private readonly Stack<AppMode> _navigationStack = new();
    private bool _loginSucceeded;

    public MainWindow()
    {
        InitializeComponent();
        TrackWindow(this);

        // Apply the user's saved theme (General settings) to the window root.
        try { ThemeApplier.Apply(Content as FrameworkElement, App.Services.GetRequiredService<ISettingsService>().Theme); }
        catch { /* theme is best-effort */ }

        // Window setup
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
        // Absolute path: a relative one resolves against the process working directory, which for a
        // packaged launch is NOT the install folder — SetIcon then fails silently to the generic glyph.
        appWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Verbinal.ico"));
        // Loc.T returns the key when no resources.pri is present (unpackaged dev run) — keep the English title then.
        var locTitle = Loc.T("MainWindow_Title");
        appWindow.Title = locTitle == "MainWindow_Title" ? "Verbinal - a CANFAR Science Portal Companion and Research Platform" : locTitle;

        _viewModel = App.Services.GetRequiredService<MainViewModel>();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.TokenExpired += OnTokenExpired;

        _legal = App.Services.GetRequiredService<ILegalAgreementService>();

        var tokenProvider = App.Services.GetRequiredService<AuthTokenProvider>();
        tokenProvider.Unauthorized += OnUnauthorized;

        // Landing view — always exists
        _landingView = new LandingView();
        _landingView.PortalRequested += async (_, _) => await NavigateByKey("portal");
        _landingView.SearchRequested += OnSearchRequested;
        _landingView.ResearchRequested += OnResearchRequested;
        _landingView.StorageRequested += (_, _) => OpenStorageBrowser();
        _landingView.NotebookRequested += (_, _) => OpenNotebook();
        _landingView.FitsViewerRequested += (_, _) => OpenFitsViewer();
        _landingView.CubeViewerRequested += (_, _) => OpenCubeViewer();
        _landingView.AiGuideRequested += (_, _) => OpenAiGuidePage();
        _landingView.WorkflowsRequested += (_, _) => OpenWorkflowsPage();
        _landingView.RemoteComputeRequested += async (_, _) => await NavigateByKey("remoteCompute");
        _landingView.AiAssistantRequested += OnAiAssistantRequested;
        LandingContainer.Child = _landingView;

        Activated += OnWindowActivated;

        InitViewStateTracking();
        InitProposalsEntryPoint();
        InitNetworkMonitor();

        ShowTermsGateIfNeeded();
        _ = ShowWelcomeIfNeededAsync(); // no-op while the terms gate is up (re-fired by OnTermsAccept)
    }

    // ── Connectivity (offline hint in the status area) ──

    private NetworkMonitor? _network;
    private bool _showingOffline;

    private void InitNetworkMonitor()
    {
        _network = new NetworkMonitor();
        _network.StatusChanged += () => DispatcherQueue.TryEnqueue(UpdateOfflineHint);
        UpdateOfflineHint();
    }

    private void UpdateOfflineHint()
    {
        if (_network is null) return;
        if (!_network.IsOnline)
        {
            _showingOffline = true;
            StatusText.Text = Loc.T("MainWindow_OfflineHint");
        }
        else if (_showingOffline)
        {
            _showingOffline = false;
            StatusText.Text = _viewModel.StatusMessage;
        }
    }

    /// <summary>Status-area setter that respects the offline hint's ownership while disconnected —
    /// without this, any auth/status PropertyChanged would silently clobber the hint.</summary>
    private void SetStatus(string text)
    {
        if (_showingOffline) return;
        StatusText.Text = text;
    }

    // ── Agent proposals (title-bar entry to the proposal strip) ──

    private McpHost? _mcpHost;
    private bool _proposalsDialogOpen;

    private void InitProposalsEntryPoint()
    {
        try { _mcpHost = App.Services.GetRequiredService<McpHost>(); }
        catch { return; } // MCP not registered (tests/dev slice) — leave the button hidden

        _mcpHost.ProposalsChanged += () => DispatcherQueue.TryEnqueue(UpdateProposalsButton);
        _mcpHost.RunningChanged += () => DispatcherQueue.TryEnqueue(UpdateProposalsButton);
        UpdateProposalsButton();
    }

    private void UpdateProposalsButton()
    {
        if (_mcpHost is null) return;
        ProposalsButton.Visibility = _mcpHost.IsRunning ? Visibility.Visible : Visibility.Collapsed;
        var pending = _mcpHost.PendingProposalCount; // count-only: no snapshot per store event
        ProposalsBadge.Visibility = pending > 0 ? Visibility.Visible : Visibility.Collapsed;
        ProposalsBadge.Value = pending;
    }

    private async void OnProposalsClick(object sender, RoutedEventArgs e)
    {
        if (_mcpHost is null || _proposalsDialogOpen) return;
        _proposalsDialogOpen = true;
        try { await AgentProposalsDialog.ShowAsync(Content.XamlRoot, _mcpHost); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Proposals dialog error: {ex.Message}"); }
        finally { _proposalsDialogOpen = false; }
    }

    private CanfarDesktop.Mcp.AppViewStateService? _viewState;

    /// <summary>Push the live navigation context to the MCP get_current_view tool (mode + open FITS paths).</summary>
    private void InitViewStateTracking()
    {
        _viewState = App.Services.GetRequiredService<CanfarDesktop.Mcp.AppViewStateService>();
        _fitsHostVm = App.Services.GetRequiredService<FitsTabHostViewModel>();
        _fitsHostVm.Tabs.CollectionChanged += (_, _) => PublishOpenFits(_fitsHostVm);
        _viewState.SetActions(NavigateByKeyAsync, SetSearchFocusActionAsync, OpenFitsActionAsync);
        _viewState.SetCubeActions(OpenCubeActionAsync, GetCubeActionAsync, SetCubeActionAsync,
                                  ExportCubeActionAsync, ProbeCubeActionAsync,
                                  ShowCubeSpectrumActionAsync, CloseCubeSpectrumActionAsync,
                                  SetCubeTransferActionAsync, GetCubeChannelProfileActionAsync,
                                  SwitchCubeTabActionAsync, ListRecentCubesActionAsync);
        _viewState.SetFitsActions(GetFitsActionAsync, SetFitsActionAsync, ProbeFitsActionAsync, GotoFitsActionAsync,
                                  BlinkFitsActionAsync, SwitchFitsTabActionAsync);
        _viewState.SetFitsBookmarkActions(ListFitsBookmarksActionAsync, SaveFitsBookmarkActionAsync, DeleteFitsBookmarkActionAsync);
        _viewState.SetNotebookActions(NotebookMutateActionAsync, GetNotebookActionAsync, GetCellOutputActionAsync,
                                      GetKernelStateActionAsync, ListNotebooksActionAsync, ListOpenNotebooksActionAsync);
        _viewState.SetTabActions(CloseTabActionAsync, ListOpenTabsActionAsync);
        _viewState.SetTabNavigationActions(CloseTabByIndexActionAsync);
        _viewState.SetSearchHost(ResolveSearchBridgeAsync);
        _viewState.SetAnnotationHost(this);
        _viewState.SetFitsFigureAction(ExportFitsFigureActionAsync);
        _viewState.SetAnnotationExportAction(ExportAnnotationsActionAsync);
        _viewState.SetUiPointerActions(PointAtUiActionAsync, ListUiTargetsActionAsync);
        _viewState.SetSettingsActions(OpenSettingsActionAsync, CloseSettingsActionAsync);
        _viewState.SetRemoteComputeActions(ShowComputeRunActionAsync, SetComputeSnippetActionAsync, GetComputeViewActionAsync);
        _viewState.SetStorageFolderAction(ShowStorageFolderActionAsync);
        Views.Controls.AgentPointer.AllClosed += _viewState.NotifyHintsDismissed;
        _viewState.SetFitsCaptureAction(CaptureFitsActionAsync);
        _viewState.SetCubeCaptureAction(CaptureCubeActionAsync);
        _viewState.SetNotebookImageAction(GetCellImageActionAsync);
        _viewState.SetCreateAnalysisNotebookAction(CreateAnalysisNotebookActionAsync);
        _viewState.AgentActivity += OnAgentActivity;
        PublishViewMode();
    }

    // ── Live ViewState write actions (invoked off-thread by the MCP tools; marshal to the UI thread) ──

    // The MCP action methods all share one shape: run `work` on the UI thread, complete a
    // TaskCompletionSource (faulting it if `work` throws), and return `fallback` if the dispatch itself
    // can't be queued. These two helpers collapse that boilerplate to a single expression per action.
    //
    // Per-call budget on the dispatch: TryEnqueue succeeding only means the callback is QUEUED — on a
    // saturated UI thread (long render, playback, a modal) it may not run for minutes, which surfaced
    // as QA F5's multi-minute tool hangs with no clean error. The budget converts that into a fast,
    // descriptive failure; a late UI completion after the timeout is simply dropped (TrySet*).
    private static readonly TimeSpan UiDispatchTimeout = TimeSpan.FromSeconds(30);

    private Task<T> OnUi<T>(Func<T> work, T fallback)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            try { tcs.TrySetResult(work()); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        }))
        {
            tcs.TrySetResult(fallback);
            return tcs.Task;
        }
        return WithUiDispatchTimeout(tcs);
    }

    private Task<T> OnUiAsync<T>(Func<Task<T>> work, T fallback)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        // try/catch INSIDE the enqueued async lambda (await inside, not around the TCS) so exception +
        // ordering behaviour matches the hand-written versions.
        if (!DispatcherQueue.TryEnqueue(async () =>
        {
            try { tcs.TrySetResult(await work()); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        }))
        {
            tcs.TrySetResult(fallback);
            return tcs.Task;
        }
        return WithUiDispatchTimeout(tcs);
    }

    private static async Task<T> WithUiDispatchTimeout<T>(TaskCompletionSource<T> tcs)
    {
        var task = tcs.Task;
        using var cts = new CancellationTokenSource();
        var delay = Task.Delay(UiDispatchTimeout, cts.Token);
        if (await Task.WhenAny(task, delay) == task)
        {
            cts.Cancel(); // release the timer
            return await task;
        }
        tcs.TrySetException(new TimeoutException(
            $"the app's UI thread did not process the request within {UiDispatchTimeout.TotalSeconds:0}s " +
            "— it may be busy (heavy rendering, playback, or an open dialog); retry shortly"));
        return await task; // faulted above, unless the UI thread won the race at the last instant
    }

    private Task<CanfarDesktop.Mcp.Tools.Write.NavigationOutcome> NavigateByKeyAsync(string mode)
        => OnUiAsync(() => NavigateByKey(mode), new CanfarDesktop.Mcp.Tools.Write.NavigationOutcome(false, mode, mode));

    /// <summary>
    /// Switch modes, and do not claim to have arrived until we have.
    ///
    /// <para>Async because two of these are: Storage builds its page by listing VOSpace over the
    /// network, and the notebook host has its own setup. Both used to be fired off as async void from
    /// a synchronous switch that returned success regardless — so navigate_to reported "storage" while
    /// the app sat on the screen it started from, and would have reported it even if the listing
    /// threw, because nothing was left to observe the exception. An agent that believed the answer
    /// then pointed at controls on a page that was not showing.</para>
    /// </summary>
    private async Task<CanfarDesktop.Mcp.Tools.Write.NavigationOutcome> NavigateByKey(string mode)
    {
        // The account's own screens open for a signed-in person only — from a tile, navigate_to, a
        // workflow step or the connect wizard alike.
        if (AccountScreens.Contains(mode) && !await SignedInAsync())
            return new(false, mode, mode, NotSignedIn);

        switch (mode)
        {
            case "landing": GoHome(); return new(true, "landing", "Home");
            case "portal": EnsureDashboard(); NavigateTo(AppMode.Portal); return new(true, "portal", "Portal");
            case "search": EnsureSearchPage(); NavigateTo(AppMode.Search); return new(true, "search", "Search");
            case "research": EnsureResearchPage(); NavigateTo(AppMode.Research); return new(true, "research", "Research");
            case "storage": return await OpenStorageBrowserCoreAsync()
                ? new(true, "storage", "Storage")
                : new(false, "storage", "Storage");
            case "notebook":
                await OpenNotebookCoreAsync(null, createNew: false);
                return new(true, "notebook", "Notebook");
            case "fitsViewer": EnsureFitsHost(); NavigateTo(AppMode.FitsViewer); return new(true, "fitsViewer", "FITS Viewer");
            case "cubeViewer": EnsureCubeHost(); NavigateTo(AppMode.CubeViewer); return new(true, "cubeViewer", "Cube Viewer");
            case "aiGuide": OpenAiGuidePage(); return new(true, "aiGuide", "AI Guide");
            case "workflows": OpenWorkflowsPage(); return new(true, "workflows", "Workflows");
            case "remoteCompute": OpenRemoteComputePage(); return new(true, "remoteCompute", "Remote Compute");
            default: return new(false, mode, mode);
        }
    }

    private Task SetSearchFocusActionAsync(double ra, double dec)
    {
        var tcs = new TaskCompletionSource();
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                EnsureSearchPage();
                _searchPage!.ViewModel.ResolvedRA = ra;
                _searchPage.ViewModel.ResolvedDec = dec;
                _searchPage.ShowSearchForm();
                NavigateTo(AppMode.Search);
                tcs.SetResult();
            }
            catch (Exception ex) { tcs.SetException(ex); }
        }))
            tcs.SetResult();
        return tcs.Task;
    }

    private Task<CanfarDesktop.Mcp.Tools.Write.OpenFitsOutcome> OpenFitsActionAsync(string id)
        => OnUiAsync(async () =>
        {
            // A local file path opens directly (the UI file-picker equivalent); otherwise resolve a
            // downloaded observation id — same dual-target behaviour as open_cube.
            string? localPath;
            string resolvedId = id;
            if (System.IO.File.Exists(id))
            {
                localPath = id;
            }
            else
            {
                var store = App.Services.GetRequiredService<ObservationStore>();
                var obs = store.Find(id);
                if (obs is null)
                    return new CanfarDesktop.Mcp.Tools.Write.OpenFitsOutcome(false, id, null,
                        "file not found and observation not in Research");
                if (!obs.FileExists)
                    return new(false, id, obs.LocalPath, "not downloaded yet — use download_observation first");
                localPath = obs.LocalPath;
                resolvedId = obs.Id;
            }
            // Await the actual parse and report opened:true only on a confirmed load — so a file that won't
            // parse (e.g. a non-FITS download) returns the real error, not optimism. But only for as long
            // as the call can wait: see WithinLoadBudget.
            var (finished, page) = await WithinLoadBudget(OpenFitsViewerAsync(localPath));
            if (!finished)
                return new(false, resolvedId, localPath,
                    "still loading — a large file takes a while; poll get_fits_view until loaded is true",
                    Loading: true);

            var error = page?.ViewModel.LoadError;
            return error is null ? new(true, resolvedId, localPath, null) : new(false, resolvedId, localPath, error);
        }, new CanfarDesktop.Mcp.Tools.Write.OpenFitsOutcome(false, id, null, "could not dispatch to UI"));

    /// <summary>
    /// How long an open waits for the file before answering "still loading" instead.
    ///
    /// Comfortably inside <see cref="UiDispatchTimeout"/>, so the call always gets to say which it is.
    /// </summary>
    private static readonly TimeSpan LoadBudget = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Wait for a load, but not past the budget. <c>Finished</c> false means it is still going.
    ///
    /// <para>The dispatch timeout races the WHOLE call, which was meant to catch a UI thread too busy
    /// to pick the call up. An open awaits a parse off that thread, so a 235 MB compressed FITS ran the
    /// clock out with the UI perfectly idle — and the error blamed "heavy rendering, playback, or an
    /// open dialog", none of which was happening, for a file that then opened fine a few seconds later.
    /// An agent told it failed tries again.</para>
    ///
    /// <para>Past the budget the load simply carries on and the call says so. Any failure still lands
    /// in the viewer's own status, where get_fits_view and get_cube_view report it.</para>
    /// </summary>
    private static async Task<(bool Finished, T? Result)> WithinLoadBudget<T>(Task<T> load)
    {
        using var cts = new CancellationTokenSource();
        if (await Task.WhenAny(load, Task.Delay(LoadBudget, cts.Token)) == load)
        {
            cts.Cancel(); // release the timer
            return (true, await load);
        }

        // Not awaited any more, so observed here instead of surfacing as an unobserved task exception.
        _ = load.ContinueWith(
            done => System.Diagnostics.Debug.WriteLine($"Load finished after its call returned: {done.Exception?.GetBaseException().Message}"),
            TaskContinuationOptions.OnlyOnFaulted);
        return (false, default);
    }

    // ── Cube Viewer MCP actions (each marshals to the UI thread) ─────────────────────────────────

    private Task<CanfarDesktop.Mcp.Tools.Write.CubeOpenOutcome> OpenCubeActionAsync(string target)
        => OnUiAsync(async () =>
        {
            var path = ResolveCubeTarget(target);
            if (path is null)
                return new CanfarDesktop.Mcp.Tools.Write.CubeOpenOutcome(false, target, 0, 0, 0,
                    "file not found, or observation not downloaded (use download_observation first)");
            var host = EnsureCubeHost();
            NavigateTo(AppMode.CubeViewer);

            var (finished, page) = await WithinLoadBudget(host.AddTabForFileAsync(path));
            if (!finished)
                return new CanfarDesktop.Mcp.Tools.Write.CubeOpenOutcome(false, path, 0, 0, 0,
                    "still loading — a large cube takes a while; poll get_cube_view until loaded is true",
                    Loading: true);

            var st = page!.GetCubeState();
            return st.Loaded
                ? new(true, path, st.Nx, st.Ny, st.Nz, null)
                : new(false, path, st.Nx, st.Ny, st.Nz, "not a 3D cube (NAXIS=3) or could not be read");
        }, new CanfarDesktop.Mcp.Tools.Write.CubeOpenOutcome(false, target, 0, 0, 0, "could not dispatch to UI"));

    private string? ResolveCubeTarget(string target)
    {
        if (System.IO.File.Exists(target)) return target;
        var store = App.Services.GetRequiredService<ObservationStore>();
        var obs = store.Find(target);
        return obs is not null && obs.FileExists ? obs.LocalPath : null;
    }

    private Task<CanfarDesktop.Services.CubeViewer.CubeViewState?> GetCubeActionAsync()
        => OnUi(() => _cubeTabHost?.ActivePage?.GetCubeState(), null);

    // ── 2D FITS viewer MCP actions (active tab) ──

    private Task<CanfarDesktop.Services.Fits.FitsViewState?> GetFitsActionAsync()
        => OnUi(() => _fitsTabHost?.GetFitsViewState(), null);

    private Task<CanfarDesktop.Services.Fits.FitsViewState?> SetFitsActionAsync(
        CanfarDesktop.Mcp.Tools.Write.FitsViewArgs args)
        => OnUi(() => _fitsTabHost?.ApplyFitsView(
            stretch: args.Stretch, colormap: args.Colormap, minCut: args.MinCut, maxCut: args.MaxCut,
            zoomPercent: args.ZoomPercent, northUp: args.NorthUp, reset: args.Reset, clearCrosshair: args.ClearCrosshair,
            selectArea: args.SelectArea,
            hdu: args.Hdu, crosshairX: args.CrosshairX, crosshairY: args.CrosshairY,
            centerX: args.CenterX, centerY: args.CenterY,
            syncZoom: args.SyncZoom, linkedCrosshair: args.LinkedCrosshair,
            showHeaderPanel: args.ShowHeaderPanel, showBookmarksPanel: args.ShowBookmarksPanel,
            showMarksPanel: args.ShowMarksPanel), null);

    private Task<CanfarDesktop.Mcp.Tools.Write.FitsBlinkOutcome?> BlinkFitsActionAsync(
        string action, int? withTabIndex, int? intervalMs)
        => OnUi<CanfarDesktop.Mcp.Tools.Write.FitsBlinkOutcome?>(
            () => _fitsTabHost?.ControlBlink(action, withTabIndex, intervalMs), null);

    private Task<CanfarDesktop.Mcp.Tools.Write.FitsTabSwitchOutcome> SwitchFitsTabActionAsync(int index)
        => OnUi(() =>
        {
            if (_fitsTabHost is null)
                return new CanfarDesktop.Mcp.Tools.Write.FitsTabSwitchOutcome(false, index, 0, null, "the FITS viewer is not open");
            bool ok = _fitsTabHost.SwitchToTab(index);
            var infos = _fitsTabHost.TabInfos();
            var active = infos.FirstOrDefault(t => t.Active)?.Name;
            return new CanfarDesktop.Mcp.Tools.Write.FitsTabSwitchOutcome(
                ok, index, infos.Count, string.IsNullOrEmpty(active) ? null : active,
                ok ? null : $"no FITS tab at index {index} ({infos.Count} open)");
        }, new CanfarDesktop.Mcp.Tools.Write.FitsTabSwitchOutcome(false, index, 0, null, "could not dispatch to UI"));

    private Task<CanfarDesktop.Services.Fits.FitsPixelResult?> ProbeFitsActionAsync(int x, int y)
        => OnUi(() => _fitsTabHost?.ProbeFitsPixel(x, y), null);

    private Task<CanfarDesktop.Services.Fits.FitsGotoOutcome> GotoFitsActionAsync(double ra, double dec)
        => OnUi(
            () => _fitsTabHost is null
                ? new CanfarDesktop.Services.Fits.FitsGotoOutcome(false, ra, dec, "the FITS viewer is not open")
                : _fitsTabHost.GotoFitsCoordinate(ra, dec),
            new CanfarDesktop.Services.Fits.FitsGotoOutcome(false, ra, dec, "could not dispatch to the UI thread"));

    // ── FITS coordinate bookmarks (routed through the host VM so the store + UI panel stay in sync) ──

    private Task<IReadOnlyList<CanfarDesktop.Services.Fits.FitsBookmark>> ListFitsBookmarksActionAsync()
        => OnUi<IReadOnlyList<CanfarDesktop.Services.Fits.FitsBookmark>>(
            () => _fitsHostVm.SavedCoordinates.Select(ToBookmark).ToList(),
            Array.Empty<CanfarDesktop.Services.Fits.FitsBookmark>());

    private Task<CanfarDesktop.Services.Fits.FitsBookmark?> SaveFitsBookmarkActionAsync(double ra, double dec, string? label, string? sourceFile)
        => OnUi(() =>
        {
            _fitsHostVm.SaveCoordinate(label ?? string.Empty, ra, dec, sourceFile);
            var saved = _fitsHostVm.SavedCoordinates.FirstOrDefault(); // SaveCoordinate inserts at index 0
            return saved is null ? null : ToBookmark(saved);
        }, null);

    private Task<bool> DeleteFitsBookmarkActionAsync(string id)
        => OnUi(() =>
        {
            var match = Guid.TryParse(id, out var gid)
                ? _fitsHostVm.SavedCoordinates.FirstOrDefault(c => c.Id == gid)
                : null;
            if (match is null) return false;
            _fitsHostVm.DeleteCoordinate(match);
            return true;
        }, false);

    private static CanfarDesktop.Services.Fits.FitsBookmark ToBookmark(CanfarDesktop.Models.Fits.SavedCoordinate c)
        => new(c.Id.ToString(), c.Label, c.Ra, c.Dec, c.SourceFile, c.SavedAt);

    private Task<CanfarDesktop.Services.CubeViewer.CubeViewState?> SetCubeActionAsync(
        CanfarDesktop.Mcp.Tools.Write.CubeViewArgs args)
        => OnUi(() =>
        {
            var cube = _cubeTabHost?.ActivePage;
            if (cube is null) return null;
            cube.ApplyCubeView(
                mode: args.Mode, channel: args.Channel, colormap: args.Colormap, stretch: args.Stretch,
                renderMode: args.RenderMode, windowLo: args.WindowLo, windowHi: args.WindowHi,
                azimuth: args.Azimuth, elevation: args.Elevation, distance: args.Distance,
                density: args.Density, spectralScale: args.SpectralScale, steps: args.Steps,
                background: args.Background, showSlicePlane: args.ShowSlicePlane, showCaptions: args.ShowCaptions,
                showPanels: args.ShowPanels,
                autoOrbit: args.AutoOrbit, playing: args.Playing, resetCamera: args.ResetCamera,
                windowPreset: args.WindowPreset, sliceZoom: args.SliceZoom,
                sliceCenterX: args.SliceCenterX, sliceCenterY: args.SliceCenterY,
                resetSliceView: args.ResetSliceView);
            return cube.GetCubeState();
        }, null);

    private Task<CanfarDesktop.Mcp.Tools.Write.CubeExportOutcome> ExportCubeActionAsync(
        CanfarDesktop.Mcp.Tools.Write.CubeExportRequest req)
        => OnUiAsync(async () =>
        {
            var cube = _cubeTabHost?.ActivePage;
            if (cube is null)
                return new CanfarDesktop.Mcp.Tools.Write.CubeExportOutcome(false, req.Path, "the cube viewer is not open (use open_cube first)");
            var err = await cube.ExportCubeToPathAsync(req.Path, req.Format, req.Scale, req.Dark,
                req.Font, req.TextColor, req.TextScale, req.Annotate, req.Transparent, req.Marks);
            return err is null ? new(true, req.Path, null) : new(false, req.Path, err);
        }, new CanfarDesktop.Mcp.Tools.Write.CubeExportOutcome(false, req.Path, "could not dispatch to UI"));

    // Typed probe outcome: NoCube when the viewer/tab isn't there; only a failed dispatch yields null.
    private Task<CanfarDesktop.Services.CubeViewer.CubeSpectrumProbe?> ProbeCubeActionAsync(int x, int y)
        => OnUi<CanfarDesktop.Services.CubeViewer.CubeSpectrumProbe?>(
            () => _cubeTabHost?.ActivePage is { } page
                ? page.ProbeCubeSpectrum(x, y)
                : new(CanfarDesktop.Services.CubeViewer.CubeProbeStatus.NoCube, null),
            null);

    private Task<CanfarDesktop.Services.CubeViewer.CubeSpectrumProbe?> ShowCubeSpectrumActionAsync(int x, int y)
        => OnUi<CanfarDesktop.Services.CubeViewer.CubeSpectrumProbe?>(
            () => _cubeTabHost?.ActivePage is { } page
                ? page.ShowCubeSpectrum(x, y)
                : new(CanfarDesktop.Services.CubeViewer.CubeProbeStatus.NoCube, null),
            null);

    private Task<bool> CloseCubeSpectrumActionAsync()
        => OnUi(() => _cubeTabHost?.ActivePage?.CloseCubeSpectrum() == true, false);

    private Task<CanfarDesktop.Services.CubeViewer.CubeViewState?> SetCubeTransferActionAsync(
        IReadOnlyList<CanfarDesktop.Services.CubeViewer.CubeTransferPoint>? points, bool reset)
        => OnUi(() => _cubeTabHost?.ActivePage?.ApplyCubeTransfer(points, reset), null);

    private Task<CanfarDesktop.Services.CubeViewer.CubeChannelProfileResult?> GetCubeChannelProfileActionAsync()
        => OnUi(() => _cubeTabHost?.ActivePage?.GetChannelProfile(), null);

    private Task<CanfarDesktop.Mcp.Tools.Write.CubeTabSwitchOutcome> SwitchCubeTabActionAsync(int index)
        => OnUi(() =>
        {
            if (_cubeTabHost is null)
                return new CanfarDesktop.Mcp.Tools.Write.CubeTabSwitchOutcome(false, index, 0, null, "the cube viewer is not open");
            bool ok = _cubeTabHost.SwitchToTab(index);
            var infos = _cubeTabHost.TabInfos();
            var active = infos.FirstOrDefault(t => t.Active)?.Name;
            return new CanfarDesktop.Mcp.Tools.Write.CubeTabSwitchOutcome(
                ok, index, infos.Count, string.IsNullOrEmpty(active) ? null : active,
                ok ? null : $"no cube tab at index {index} ({infos.Count} open)");
        }, new CanfarDesktop.Mcp.Tools.Write.CubeTabSwitchOutcome(false, index, 0, null, "could not dispatch to UI"));

    // Recents persist on disk, so they are listable even before the cube viewer host exists.
    private Task<IReadOnlyList<CanfarDesktop.Mcp.Tools.Write.RecentCubeInfo>> ListRecentCubesActionAsync()
        => OnUi<IReadOnlyList<CanfarDesktop.Mcp.Tools.Write.RecentCubeInfo>>(
            () => (_cubeTabHost?.RecentCubes ?? new CanfarDesktop.Services.CubeViewer.RecentCubesService().Entries)
                .Select(e => new CanfarDesktop.Mcp.Tools.Write.RecentCubeInfo(e.Name, e.Path, e.OpenedAt))
                .ToList(),
            Array.Empty<CanfarDesktop.Mcp.Tools.Write.RecentCubeInfo>());

    // ── Tab management (close the active viewer tab / count open tabs) ──
    private Task<TabCloseOutcome> CloseTabActionAsync(string kind)
        => OnUi(() =>
        {
            switch (kind)
            {
                case "notebook":
                    if (_notebookTabHost?.ViewModel.ActiveViewModel is null)
                        return new TabCloseOutcome(false, kind, "no notebook tab is open");
                    _notebookTabHost.DiscardActiveTab(); // no save prompt; autosave keeps a recovery copy
                    return new TabCloseOutcome(true, kind, null);
                case "fits":
                {
                    var closed = _fitsTabHost?.CloseActiveTab() == true;
                    return new TabCloseOutcome(closed, kind, closed ? null : "no FITS tab is open");
                }
                case "cube":
                {
                    var closed = _cubeTabHost?.CloseActiveTab() == true;
                    return new TabCloseOutcome(closed, kind, closed ? null : "no cube tab is open");
                }
                default:
                    return new TabCloseOutcome(false, kind, "unknown kind");
            }
        }, new TabCloseOutcome(false, kind, "could not dispatch to UI"));

    private Task<OpenTabsState> ListOpenTabsActionAsync()
        => OnUi(() => new OpenTabsState(
            _notebookTabHost?.ViewModel.Tabs.Count ?? 0,
            _fitsHostVm?.Tabs.Count ?? 0,
            _cubeTabHost?.OpenTabCount ?? 0,
            _cubeTabHost?.TabInfos(),
            _fitsTabHost?.TabInfos()),
            new OpenTabsState(0, 0, 0));

    // ── Analysis-notebook hand-off (SCI-10): resolve the downloaded observation, seed an .ipynb, open it ──
    private Task<NotebookState?> CreateAnalysisNotebookActionAsync(string observationId, string template)
        => OnUiAsync<NotebookState?>(async () =>
        {
            var store = App.Services.GetRequiredService<ObservationStore>();
            var obs = store.Find(observationId);
            if (obs is null) return null; // not in Research — the agent must download_observation first

            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Verbinal");
            Directory.CreateDirectory(dir);
            var stem = string.IsNullOrEmpty(obs.ObservationID) ? obs.PublisherID : obs.ObservationID;
            var safe = string.Concat(("analysis-" + stem).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            var file = Path.Combine(dir, safe + ".ipynb");

            await File.WriteAllTextAsync(file, NotebookParser.Serialize(AnalysisNotebookBuilder.Build(obs, template)));

            return await OpenNotebookCoreAsync(file, createNew: false) is not null ? _notebookTabHost?.GetNotebookState() : null;
        }, null);

    /// <summary>Map the current AppMode to the MCP mode name + title and publish it (UI thread).</summary>
    private void PublishViewMode()
    {
        var (mode, title) = _currentMode switch
        {
            AppMode.Landing => ("landing", "Home"),
            AppMode.Portal => ("portal", "Portal"),
            AppMode.Search => ("search", "Search"),
            AppMode.Research => ("research", "Research"),
            AppMode.Storage => ("storage", "Storage"),
            AppMode.Notebook => ("notebook", "Notebook"),
            AppMode.FitsViewer => ("fitsViewer", "FITS Viewer"),
            AppMode.CubeViewer => ("cubeViewer", "Cube Viewer"),
            AppMode.ObservationDetail => ("observationDetail", "Observation"),
            AppMode.AiGuide => ("aiGuide", "AI Guide"),
            AppMode.Workflows => ("workflows", "Workflows"),
            AppMode.RemoteCompute => ("remoteCompute", "Remote Compute"),
            _ => ("landing", "Home"),
        };
        _viewState?.SetMode(mode, title);

        // Title-bar section subtitle: name the app you're inside (localized), hidden on the launchpad.
        SectionText.Text = _currentMode == AppMode.Landing
            ? string.Empty
            : Loc.F("MainWindow_SectionFormat", TitleForModule(mode));
        SectionText.Visibility = _currentMode == AppMode.Landing ? Visibility.Collapsed : Visibility.Visible;
    }

    private void PublishOpenFits(FitsTabHostViewModel host)
        => _viewState?.SetOpenFitsPaths(host.Tabs
            .Select(t => t.ViewModel.FilePath)
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToList());

    // ── "Agent is working" indicator ─────────────────────────────────────────
    // Raised off the MCP connection thread on each agent tool call; marshal to the UI thread, show the
    // pill, and (re)arm an idle timer that hides it once the agent has been quiet for a couple of seconds.

    private DispatcherTimer? _agentActivityTimer;

    /// <summary>
    /// Turns a stream of tool calls into the two moments worth hearing.
    ///
    /// The activity signal arrives once per call and an agent doing real work raises it many times a
    /// second, so playing each one would be a stutter rather than a cue. The tracker collapses a burst
    /// into one "started" and — when the same idle timer that hides the indicator runs out — one
    /// "finished".
    /// </summary>
    private readonly Helpers.AgentCueTracker _agentCues = new();

    private void OnAgentActivity(CanfarDesktop.Mcp.AppViewStateService.AgentActivitySignal signal)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            AgentActivityText.Text = signal.Module is { } module
                ? Loc.F("MainWindow_AgentWorkingModule", TitleForModule(module))
                : Loc.T("MainWindow_AgentWorking");
            AgentActivityIndicator.Visibility = Visibility.Visible;

            // On the EDGE of an agent starting, not on every call it makes.
            if (_agentCues.Activity() is { } cue) Helpers.AgentSounds.Play(cue);

            _agentActivityTimer ??= CreateAgentActivityTimer();
            _agentActivityTimer.Stop();
            _agentActivityTimer.Start();
        });
    }

    private DispatcherTimer CreateAgentActivityTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            AgentActivityIndicator.Visibility = Visibility.Collapsed;
            if (_agentCues.Idle() is { } cue) Helpers.AgentSounds.Play(cue);
        };
        return timer;
    }

    private static string TitleForModule(string mode) => mode switch
    {
        "search" => Loc.T("Module_Search"),
        "portal" => Loc.T("Module_Portal"),
        "storage" => Loc.T("Module_Storage"),
        "research" => Loc.T("Module_Research"),
        "fitsViewer" => Loc.T("Module_FitsViewer"),
        "cubeViewer" => Loc.T("Module_CubeViewer"),
        "notebook" => Loc.T("Module_Notebook"),
        "workflows" => Loc.T("Module_Workflows"),
        "remoteCompute" => Loc.T("Module_RemoteCompute"),
        "aiGuide" => Loc.T("Module_AiGuide"),
        "observationDetail" => Loc.T("Module_ObservationDetail"),
        _ => Loc.T("Module_App"),
    };

    #region File Browser Panel

    public void ToggleFilePanel()
    {
        _filePanelVisible = !_filePanelVisible;

        if (_filePanelVisible && _filePanel is null)
        {
            var vm = App.Services.GetRequiredService<LocalFileBrowserViewModel>();
            _filePanel = new LocalFileBrowserPanel(vm);
            _filePanel.FileOpenRequested += OnFilePanelFileOpen;
            FilePanelContainer.Child = _filePanel;

            // Default root: user's Documents folder
            var docsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            vm.SetRootPath(docsPath);
        }

        FilePanelColumn.Width = _filePanelVisible
            ? new Microsoft.UI.Xaml.GridLength(280)
            : new Microsoft.UI.Xaml.GridLength(0);
    }

    public void SetFilePanelRoot(string path)
    {
        if (_filePanel is not null)
            _filePanel.ViewModel.SetRootPath(path);
    }

    private void OnFilePanelFileOpen(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext is ".ipynb" or ".py" or ".md")
        {
            OpenNotebook(filePath);
        }
        else if (ext is ".fits" or ".fit" or ".fts")
        {
            OpenFitsViewer(filePath);
        }
        else
        {
            // Open with system default app — block executable extensions
            var fileExt = Path.GetExtension(filePath).ToLowerInvariant();
            if (fileExt is ".exe" or ".bat" or ".cmd" or ".ps1" or ".vbs" or ".js" or ".msi" or ".com" or ".scr")
            {
                System.Diagnostics.Debug.WriteLine($"Blocked shell execute for: {fileExt}");
                return;
            }
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Open file failed: {ex.Message}");
            }
        }
    }

    #endregion

    #region Initialization

    private async void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnWindowActivated;
        await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            SetAuthProgress(true);
            await _viewModel.InitializeAsync();
            SetAuthProgress(false);
            UpdateAuthUI();
            _landingView.StatusMessage = _viewModel.StatusMessage;
            // Stay on Landing — user chooses where to go
        }
        catch (Exception ex)
        {
            SetAuthProgress(false);
            StatusText.Text = Loc.F("MainWindow_StartupError", ex.Message);
        }
    }

    #endregion

    #region Navigation

    private void NavigateTo(AppMode mode)
    {
        if (_currentMode != mode)
            _navigationStack.Push(_currentMode);
        ApplyMode(mode);
    }

    private Border ContainerFor(AppMode mode) => mode switch
    {
        AppMode.Portal => PortalContainer,
        AppMode.Search => SearchContainer,
        AppMode.Research => ResearchContainer,
        AppMode.Storage => StorageContainer,
        AppMode.Notebook => NotebookContainer,
        AppMode.FitsViewer => FitsViewerContainer,
        AppMode.ObservationDetail => ObsDetailContainer,
        AppMode.CubeViewer => CubeViewerContainer,
        AppMode.AiGuide => AiGuideContainer,
        AppMode.Workflows => WorkflowsContainer,
        AppMode.RemoteCompute => RemoteComputeContainer,
        _ => LandingContainer,
    };

    /// <summary>Shared visibility swap for forward, back, and home navigation.</summary>
    private void ApplyMode(AppMode mode)
    {
        // A hint points at a control on the page being left, so it goes the instant the page does —
        // not when its timer happens to run out. A tail reaching across a screen that has changed
        // underneath it points at whatever is now in that spot, which is worse than no hint.
        Views.Controls.AgentPointer.CloseAll();

        var target = ContainerFor(mode);
        var appearing = target.Visibility == Visibility.Collapsed;

        _currentMode = mode;
        LandingContainer.Visibility = mode == AppMode.Landing ? Visibility.Visible : Visibility.Collapsed;
        PortalContainer.Visibility = mode == AppMode.Portal ? Visibility.Visible : Visibility.Collapsed;
        SearchContainer.Visibility = mode == AppMode.Search ? Visibility.Visible : Visibility.Collapsed;
        ResearchContainer.Visibility = mode == AppMode.Research ? Visibility.Visible : Visibility.Collapsed;
        StorageContainer.Visibility = mode == AppMode.Storage ? Visibility.Visible : Visibility.Collapsed;
        NotebookContainer.Visibility = mode == AppMode.Notebook ? Visibility.Visible : Visibility.Collapsed;
        FitsViewerContainer.Visibility = mode == AppMode.FitsViewer ? Visibility.Visible : Visibility.Collapsed;
        ObsDetailContainer.Visibility = mode == AppMode.ObservationDetail ? Visibility.Visible : Visibility.Collapsed;
        CubeViewerContainer.Visibility = mode == AppMode.CubeViewer ? Visibility.Visible : Visibility.Collapsed;
        AiGuideContainer.Visibility = mode == AppMode.AiGuide ? Visibility.Visible : Visibility.Collapsed;
        WorkflowsContainer.Visibility = mode == AppMode.Workflows ? Visibility.Visible : Visibility.Collapsed;
        RemoteComputeContainer.Visibility = mode == AppMode.RemoteCompute ? Visibility.Visible : Visibility.Collapsed;

        BackButton.Visibility = _navigationStack.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        PublishViewMode();

        if (appearing)
        {
            AnimateIn(target);
            FocusShownView(target);
        }
    }

    /// <summary>Short fade so view changes don't read as an abrupt hard cut (reduce-motion aware).</summary>
    private static void AnimateIn(UIElement element) => AppMotion.FadeIn(element);

    /// <summary>
    /// The previously focused element just collapsed with the old view, orphaning
    /// keyboard focus on the window; move it into the newly shown view so Tab and
    /// arrow keys keep working.
    /// </summary>
    private void FocusShownView(DependencyObject container)
    {
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (Microsoft.UI.Xaml.Input.FocusManager.FindFirstFocusableElement(container) is { } target)
                _ = Microsoft.UI.Xaml.Input.FocusManager.TryFocusAsync(target, FocusState.Programmatic);
        });
    }

    private void OnToggleFilePanel(object sender, RoutedEventArgs e) => ToggleFilePanel();

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (_navigationStack.Count > 0)
            ApplyMode(_navigationStack.Pop());
    }

    private void OnHomeClick(object sender, RoutedEventArgs e) => GoHome();

    private void GoHome()
    {
        _navigationStack.Clear();
        ApplyMode(AppMode.Landing);
    }

    private void OnSearchRequested(object? sender, EventArgs e)
    {
        EnsureSearchPage();
        NavigateTo(AppMode.Search);
    }

    private void OnResearchRequested(object? sender, EventArgs e)
    {
        EnsureResearchPage();
        NavigateTo(AppMode.Research);
    }

    private void EnsureAiGuidePage()
    {
        if (_aiGuidePage is not null) return;
        _aiGuidePage = App.Services.GetRequiredService<AiGuidePage>();
        AiGuideContainer.Child = _aiGuidePage;
        _aiGuidePage.LoadAsync();
    }

    private void OpenAiGuidePage()
    {
        EnsureAiGuidePage();
        NavigateTo(AppMode.AiGuide);
    }

    private void OpenWorkflowsPage()
    {
        EnsureWorkflowsPage();
        NavigateTo(AppMode.Workflows);
    }

    private void EnsureWorkflowsPage()
    {
        if (_workflowsPage is not null) return;
        _workflowsPage = App.Services.GetRequiredService<Views.WorkflowsPage>();
        // Step "View:" deep-links route through the same key navigation the MCP navigate tool uses.
        // Fire and forget: a deep link is a request to go there, and nothing here waits on arrival.
        _workflowsPage.NavigateRequested += key => _ = NavigateByKey(key);
        WorkflowsContainer.Child = _workflowsPage;
    }

    private Views.RemoteComputePage ShowRemoteComputePage()
    {
        EnsureRemoteComputePage();
        NavigateTo(AppMode.RemoteCompute);
        return _remoteComputePage!;
    }

    // Read afresh on every visit: the session may have been started or stopped from the Portal, or by
    // an assistant, since the page was last on screen.
    private void OpenRemoteComputePage() => _ = ShowRemoteComputePage().RefreshAsync();

    // ── Remote Compute and Storage, for an agent ──

    /// <summary>
    /// Bring the Remote Compute screen up and wait for it to know where compute stands — until it has,
    /// it shows neither its setup steps nor its runs, and pointing at either would find nothing. Null
    /// when the person was asked to sign in and did not.
    /// </summary>
    private async Task<Views.RemoteComputePage?> RemoteComputeOnScreenAsync()
    {
        if (!await SignedInAsync()) return null;
        var page = ShowRemoteComputePage();
        await page.RefreshAsync();
        return page;
    }

    private Task<CanfarDesktop.Mcp.Tools.Write.ComputeScreenView> ShowComputeRunActionAsync(string? executionId)
        => OnUiAsync(async () => await RemoteComputeOnScreenAsync() is { } page
                ? await page.ShowRunAsync(executionId)
                : CanfarDesktop.Mcp.Tools.Write.ComputeScreenView.Unavailable(NotSignedIn),
            NoComputeWindow);

    private Task<CanfarDesktop.Mcp.Tools.Write.ComputeScreenView> SetComputeSnippetActionAsync(
        CanfarDesktop.Mcp.Tools.Write.ComputeSnippetRequest request)
        => OnUiAsync(async () => await RemoteComputeOnScreenAsync() is { } page
                ? page.SetSnippet(request)
                : CanfarDesktop.Mcp.Tools.Write.ComputeScreenView.Unavailable(NotSignedIn),
            NoComputeWindow);

    private Task<CanfarDesktop.Mcp.Tools.Write.ComputeScreenView> GetComputeViewActionAsync()
        => OnUi(() => _remoteComputePage?.Capture(_currentMode == AppMode.RemoteCompute)
                      ?? NoComputeWindow with { Message = "the Remote Compute screen has not been opened" },
                NoComputeWindow);

    private static CanfarDesktop.Mcp.Tools.Write.ComputeScreenView NoComputeWindow
        => CanfarDesktop.Mcp.Tools.Write.ComputeScreenView.Unavailable("could not dispatch to UI");

    private Task<CanfarDesktop.Mcp.Tools.Write.StorageFolderShown> ShowStorageFolderActionAsync(string folder)
        => OnUiAsync(async () =>
        {
            if (Helpers.StorageFolder.RelativeToHome(folder, _viewModel.Username) is not { } relative)
                return new CanfarDesktop.Mcp.Tools.Write.StorageFolderShown(false, folder,
                    "the Storage screen shows your own home; read other areas with list_vospace_path");

            return await OpenStorageBrowserCoreAsync(relative)
                ? new CanfarDesktop.Mcp.Tools.Write.StorageFolderShown(true, relative)
                : new CanfarDesktop.Mcp.Tools.Write.StorageFolderShown(false, relative, "sign-in was declined");
        }, new CanfarDesktop.Mcp.Tools.Write.StorageFolderShown(false, folder, "could not dispatch to UI"));

    private void EnsureRemoteComputePage()
    {
        if (_remoteComputePage is not null) return;
        _remoteComputePage = App.Services.GetRequiredService<Views.RemoteComputePage>();
        _remoteComputePage.OpenFolderRequested += folder => _ = OpenStorageBrowserCoreAsync(folder);
        RemoteComputeContainer.Child = _remoteComputePage;
    }

    private void EnsureDashboard()
    {
        if (_dashboardPage is not null) return;
        _dashboardPage = App.Services.GetRequiredService<DashboardPage>();
        PortalContainer.Child = _dashboardPage;
        _ = _dashboardPage.LoadDataAsync(_viewModel.Username);
    }

    private void EnsureSearchPage()
    {
        if (_searchPage is not null) return;
        _searchPage = App.Services.GetRequiredService<SearchPage>();
        _searchPage.ObservationDetailRequested += OpenObservationDetail;
        SearchContainer.Child = _searchPage;

        // Surface the Search form's resolved sky focus to the MCP get_current_view tool.
        var searchVm = _searchPage.ViewModel;
        searchVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SearchViewModel.ResolvedRA) or nameof(SearchViewModel.ResolvedDec))
                _viewState?.SetSearchFocus(searchVm.ResolvedRA, searchVm.ResolvedDec);
        };
        _viewState?.SetSearchFocus(searchVm.ResolvedRA, searchVm.ResolvedDec);

        _searchPage.LoadAsync();
    }

    public void OpenObservationDetail(string publisherID)
    {
        if (string.IsNullOrEmpty(publisherID)) return;
        if (_obsDetailPage is null)
        {
            _obsDetailPage = App.Services.GetRequiredService<ObservationDetailPage>();
            _obsDetailPage.SignInRequested += OnObsDetailSignIn;
            _obsDetailPage.CloseRequested += () => OnBackClick(this, new RoutedEventArgs());
            _obsDetailPage.OpenInCubeRequested += path => OpenCubeViewer(path);
            _obsDetailPage.OpenInFitsRequested += path => _ = OpenFitsViewerAsync(path);
            _obsDetailPage.ViewInResearchRequested += () => { EnsureResearchPage(); NavigateTo(AppMode.Research); };
            ObsDetailContainer.Child = _obsDetailPage;
        }
        NavigateTo(AppMode.ObservationDetail);
        _ = _obsDetailPage.LoadAsync(publisherID);
    }

    private async void OnObsDetailSignIn()
    {
        try
        {
            if (await ShowLoginDialogAsync() && _obsDetailPage is not null)
                await _obsDetailPage.RefreshAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Observation detail sign-in error: {ex.Message}");
        }
    }

    public async void OpenStorageBrowser() => await OpenStorageBrowserCoreAsync();

    /// <summary>
    /// Open Storage, and say whether it opened.
    ///
    /// False when a signed-out user dismisses the login dialog — which is a refusal, not a failure,
    /// and used to be reported to an agent as a successful navigation.
    /// </summary>
    private async Task<bool> OpenStorageBrowserCoreAsync(string? folder = null)
    {
        if (!await SignedInAsync()) return false;

        if (_storagePage is null)
        {
            _storagePage = App.Services.GetRequiredService<StorageBrowserPage>();
            _storagePage.OpenInFitsViewerRequested += path => OpenFitsViewer(path);
            _storagePage.OpenInCubeViewerRequested += path => OpenCubeViewer(path);
            StorageContainer.Child = _storagePage;

            // Shown first, filled second. Listing VOSpace is a network round trip, and awaiting it
            // before navigating meant the app sat on the previous screen for as long as the server
            // took — past the agent dispatch budget on a cold first open, so navigate_to reported a
            // thirty-second timeout for a page that was going to arrive perfectly well. The page has
            // its own spinner for precisely this; the empty folder list is what it is for.
            NavigateTo(AppMode.Storage);
            _ = _storagePage.LoadAsync(_viewModel.Username, folder ?? string.Empty);
            return true;
        }

        NavigateTo(AppMode.Storage);
        if (folder is not null) _ = _storagePage.ViewModel.NavigateToAsync(folder);
        return true;
    }

    private Views.FitsViewer.FitsTabHost? _fitsTabHost;
    private FitsTabHostViewModel _fitsHostVm = null!; // singleton; owns the saved-coordinate bookmarks (set during MCP init)
    private NotebookTabHost? _notebookTabHost;

    // Serialize notebook mutations so concurrently-pipelined MCP calls can't interleave across awaits
    // (the run/save/kernel ops yield the UI pump) and corrupt the active tab's index/selection state.
    private readonly System.Threading.SemaphoreSlim _notebookMutateGate = new(1, 1);

    public async void OpenNotebook(string? filePath = null)
    {
        try { await OpenNotebookCoreAsync(filePath, createNew: false); }
        catch (Exception ex) { StatusText.Text = Loc.F("MainWindow_NotebookError", ex.Message); }
    }

    /// <summary>Ensure the notebook host exists, open <paramref name="filePath"/> (or a new tab if
    /// <paramref name="createNew"/>), switch to the notebook module, and return the active notebook view model.</summary>
    private async Task<ViewModels.Notebook.NotebookViewModel?> OpenNotebookCoreAsync(string? filePath, bool createNew)
    {
        if (_notebookTabHost is null)
        {
            var hostVm = App.Services.GetRequiredService<ViewModels.Notebook.NotebookTabHostViewModel>();
            _notebookTabHost = new NotebookTabHost(hostVm);
            // GoHome (not NavigateTo) so the empty host doesn't stay on the back stack.
            _notebookTabHost.AllTabsClosed += GoHome;
            NotebookContainer.Child = _notebookTabHost;
            await _notebookTabHost.CheckRecoveryAsync();
        }

        if (filePath is not null)
        {
            await _notebookTabHost.AddTabForFileAsync(filePath);
            NavigateTo(AppMode.Notebook);
            // A successful load sets FilePath; if it's still null the open failed (bad path / parse error).
            // Drop the orphan "Untitled" tab so failures don't accumulate empty tabs, and signal failure (null).
            var loaded = _notebookTabHost.ViewModel.ActiveViewModel;
            if (loaded?.FilePath is null)
            {
                _notebookTabHost.DiscardActiveTab();
                return null;
            }
            return loaded;
        }

        if (createNew)
            _notebookTabHost.AddNewTab();

        NavigateTo(AppMode.Notebook);
        return _notebookTabHost.ViewModel.ActiveViewModel;
    }

    // ── Notebook MCP actions (active tab; mutations dispatched through one applier on the UI thread) ──

    private Task<NotebookState?> NotebookMutateActionAsync(NotebookCommand cmd)
        => OnUiAsync(() => ApplyNotebookCommandAsync(cmd), null);

    // Gate + open/create routing stay here (shell navigation + host instantiation a UserControl can't do
    // to itself); the active-tab cell/kernel logic lives on NotebookTabHost (viewer owns its MCP logic).
    private async Task<NotebookState?> ApplyNotebookCommandAsync(NotebookCommand cmd)
    {
        // interrupt/restart BYPASS the mutate gate: they are the unwedge tools. A run_cell stuck on a
        // never-returning cell holds the gate indefinitely — serializing the interrupt behind it would
        // make the agent's only remedies deadlock too (the exact wedge this guards against).
        if (cmd.Op is NotebookOp.InterruptKernel or NotebookOp.RestartKernel)
            return _notebookTabHost is null ? null : await _notebookTabHost.ApplyNotebookCommandAsync(cmd);

        // Bounded wait, not infinite: a wedged execution must surface as an actionable error, not
        // silently queue every later notebook call from every client forever.
        if (!await _notebookMutateGate.WaitAsync(TimeSpan.FromSeconds(30)))
            throw new InvalidOperationException(
                "Another notebook operation is still running (a cell may be stuck executing). " +
                "Use interrupt_kernel or restart_kernel to unblock it, then retry.");
        try
        {
            switch (cmd.Op)
            {
                case NotebookOp.Open:
                    if (await OpenNotebookCoreAsync(cmd.Path, createNew: false) is null)
                        throw new InvalidOperationException($"Could not open notebook: {cmd.Path}");
                    return _notebookTabHost!.GetNotebookState();
                case NotebookOp.Create:
                    return await OpenNotebookCoreAsync(null, createNew: true) is not null ? _notebookTabHost!.GetNotebookState() : null;
                default:
                    return _notebookTabHost is null ? null : await _notebookTabHost.ApplyNotebookCommandAsync(cmd);
            }
        }
        finally
        {
            _notebookMutateGate.Release();
        }
    }

    private Task<NotebookState?> GetNotebookActionAsync(string? notebook)
        => OnUi(() => _notebookTabHost?.GetNotebookState(notebook), null);

    private Task<NotebookCellOutputs?> GetCellOutputActionAsync(int index, string? notebook)
        => OnUi(() => _notebookTabHost?.GetCellOutputs(index, notebook), null);

    private Task<NotebookKernelInfo> GetKernelStateActionAsync(string? notebook)
        => OnUi(() => _notebookTabHost?.GetKernelInfo(notebook) ?? new NotebookKernelInfo("Dead", "no notebook open", ""),
                new NotebookKernelInfo("Dead", "could not dispatch", ""));

    private Task<IReadOnlyList<OpenNotebookInfo>> ListOpenNotebooksActionAsync()
        => OnUi<IReadOnlyList<OpenNotebookInfo>>(
            () => _notebookTabHost?.ListOpenNotebooks() ?? Array.Empty<OpenNotebookInfo>(),
            Array.Empty<OpenNotebookInfo>());

    private Task<IReadOnlyList<NotebookRef>> ListNotebooksActionAsync()
    {
        // RecentNotebooksService is a thread-safe singleton — no UI-thread marshaling needed.
        var recent = App.Services.GetRequiredService<RecentNotebooksService>();
        IReadOnlyList<NotebookRef> list = recent.Entries
            .Select(e => new NotebookRef(e.Path, e.Name, e.OpenedAt)).ToList();
        return Task.FromResult(list);
    }

    private Views.CubeViewer.CubeTabHost? _cubeTabHost;

    private Views.CubeViewer.CubeTabHost EnsureCubeHost()
    {
        if (_cubeTabHost is null)
        {
            _cubeTabHost = new Views.CubeViewer.CubeTabHost();
            CubeViewerContainer.Child = _cubeTabHost;
        }
        return _cubeTabHost;
    }

    /// <summary>
    /// Open the tabbed 3D Cube Viewer. When <paramref name="filePath"/> is given, that FITS spectral
    /// cube opens in a new tab (from Open, Search, Research, Storage, or an MCP tool); otherwise the
    /// viewer is shown as-is (its empty-state prompt if no cubes are open).
    /// </summary>
    public void OpenCubeViewer(string? filePath = null)
    {
        try
        {
            var host = EnsureCubeHost();
            NavigateTo(AppMode.CubeViewer);
            if (!string.IsNullOrEmpty(filePath))
                _ = host.AddTabForFileAsync(filePath);
        }
        catch (Exception ex)
        {
            StatusText.Text = Loc.F("MainWindow_CubeViewerError", ex.Message);
        }
    }

    public async void OpenFitsViewer(string? filePath = null)
    {
        try { await OpenFitsViewerAsync(filePath); }
        catch (Exception ex) { StatusText.Text = Loc.F("MainWindow_FitsViewerError", ex.Message); }
    }

    /// <summary>
    /// Ensure the FITS tab host exists, open <paramref name="filePath"/> in a new tab (awaiting the
    /// actual parse), navigate to the viewer, and return the loaded page so the caller can inspect
    /// the real load outcome (<see cref="FitsViewerViewModel.LoadError"/>). Returns null when no file.
    /// </summary>
    private async Task<Views.FitsViewer.FitsViewerPage?> OpenFitsViewerAsync(string? filePath)
    {
        var host = EnsureFitsHost();

        Views.FitsViewer.FitsViewerPage? page = null;
        if (filePath is not null)
        {
            page = await host.AddTabForFileAsync(filePath);
            // The tab's FilePath is only set during the (async) load — after the CollectionChanged publish
            // already ran with an empty path. Re-publish now so get_current_view.openFitsPaths is correct.
            PublishOpenFits(host.ViewModel);
        }

        NavigateTo(AppMode.FitsViewer);
        return page;
    }

    /// <summary>
    /// Build the FITS host if it is not there yet.
    ///
    /// Its own method because navigating to the viewer needs it as much as opening a file does. It
    /// used to be built only on the way in with a file, so going to the FITS viewer with nothing open
    /// showed an empty container — and the "No image open" state, which exists for exactly that
    /// moment and offers the Open button and the recent files, lives INSIDE the host and so could
    /// never appear.
    /// </summary>
    private Views.FitsViewer.FitsTabHost EnsureFitsHost()
    {
        if (_fitsTabHost is null)
        {
            var hostVm = App.Services.GetRequiredService<FitsTabHostViewModel>();
            _fitsTabHost = new Views.FitsViewer.FitsTabHost(hostVm);
            _fitsTabHost.SearchAtPositionRequested += OnSearchAtFitsPosition;
            // GoHome (not NavigateTo) so the empty host doesn't stay on the back stack.
            _fitsTabHost.AllTabsClosed += GoHome;
            FitsViewerContainer.Child = _fitsTabHost;
        }
        return _fitsTabHost;
    }

    private void OnSearchAtFitsPosition(double ra, double dec)
    {
        EnsureSearchPage();
        // Set RA/Dec directly — no need for name resolution
        if (_searchPage is not null)
        {
            var vm = _searchPage.ViewModel;
            // Suppress resolver: set NONE so Target change doesn't trigger async resolve
            var prevService = vm.ResolverService;
            vm.ResolverService = "NONE";

            // The box gets the form Search itself parses. It used to get the CADC resolver's packed
            // form — "16:00:0000,+48:00:000", one token with the seconds digit-packed — which the
            // search parser cannot read, so the search only worked because ResolvedRA/Dec happened to
            // be set alongside it. Anything that cleared those (retyping, a saved query reloaded) left
            // text that fell through to a target-NAME match and searched for an observation called
            // that. Now the text in the box is sufficient on its own, and it is the same string Copy
            // coordinates puts on the clipboard, so pasting and "Search here" cannot disagree.
            vm.Target = Helpers.MarkClipboard.Sky(ra, dec);
            vm.ResolvedRA = ra;
            vm.ResolvedDec = dec;
            vm.ResolverStatus = Loc.T("Search_FromFitsViewer");
            vm.ResolverService = prevService;
            // Surface the form with the filled coordinates — the page may have been left on the
            // Results or ADQL tab, which is where the user was otherwise landing.
            _searchPage.ShowSearchForm();
        }
        NavigateTo(AppMode.Search);
    }

    private void EnsureResearchPage()
    {
        if (_researchPage is null)
        {
            _researchPage = App.Services.GetRequiredService<ResearchPage>();
            _researchPage.ViewModel.ViewInFitsRequested += path => OpenFitsViewer(path);
            _researchPage.ViewModel.ViewInCubeRequested += path => OpenCubeViewer(path);
            ResearchContainer.Child = _researchPage;
        }
        else
        {
            _researchPage.RefreshList();
        }
    }

    // Guards the single-ContentDialog constraint: double-clicking a login-gated
    // tile (or the Login button) would otherwise open two dialogs and throw.
    private bool _loginDialogOpen;

    /// <summary>
    /// Whether somebody is signed in — asking them to when nobody is. Every way into an account screen
    /// (see <see cref="AccountScreens"/>) passes through here.
    /// </summary>
    private async Task<bool> SignedInAsync() => _viewModel.IsAuthenticated || await ShowLoginDialogAsync();

    /// <summary>What an agent is told when the person was asked to sign in and did not.</summary>
    private const string NotSignedIn =
        "the person did not sign in; Portal, Remote Compute and Storage open only for a signed-in account";

    /// <summary>Show login dialog. Returns true if login succeeded.</summary>
    private async Task<bool> ShowLoginDialogAsync()
    {
        if (_loginDialogOpen) return false;
        _loginDialogOpen = true;
        LoginViewModel? loginVm = null;
        try
        {
            loginVm = App.Services.GetRequiredService<LoginViewModel>();
            _loginSucceeded = false;
            loginVm.LoginSucceeded += OnLoginSucceeded;

            var dialog = new LoginDialog(loginVm) { XamlRoot = Content.XamlRoot };
            await dialog.ShowAsync();
            return _loginSucceeded;
        }
        catch (Exception ex)
        {
            StatusText.Text = Loc.F("MainWindow_LoginError", ex.Message);
            return false;
        }
        finally
        {
            _loginDialogOpen = false;
            if (loginVm is not null)
                loginVm.LoginSucceeded -= OnLoginSucceeded;
        }
    }

    #endregion

    #region Auth state

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            // UserInfo arrives after IsAuthenticated on startup restore — refresh the identity menu when it lands.
            if (e.PropertyName is "IsAuthenticated" or "Username" or "StatusMessage" or "UserInfo")
            {
                UpdateAuthUI();
                _landingView.StatusMessage = _viewModel.StatusMessage;
                // Any successful sign-in (dialog, silent re-auth, tile-gated login) clears the bar.
                if (_viewModel.IsAuthenticated) SessionExpiredBar.IsOpen = false;
            }
        });
    }

    // Collapse the ring when idle — an inactive ProgressRing still occupies its
    // 20px slot, leaving a permanent dead gap in the title bar.
    private void SetAuthProgress(bool active)
    {
        AuthProgress.IsActive = active;
        AuthProgress.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateAuthUI()
    {
        SetStatus(_viewModel.StatusMessage);
        _landingView.SetAuthenticated(_viewModel.IsAuthenticated);

        if (_viewModel.IsAuthenticated)
        {
            LoginButton.Visibility = Visibility.Collapsed;
            UserButton.Visibility = Visibility.Visible;
            UserButton.Content = _viewModel.Username;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
                UserButton, Loc.F("MainWindow_AccountOptions", _viewModel.Username));

            var info = _viewModel.UserInfo;
            var displayName = string.Join(" ",
                new[] { info?.FirstName, info?.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
            UserNameMenuItem.Text = string.IsNullOrWhiteSpace(displayName) ? _viewModel.Username : displayName;
            UserEmailMenuItem.Text = info?.Email ?? "";
            UserEmailMenuItem.Visibility =
                string.IsNullOrWhiteSpace(info?.Email) ? Visibility.Collapsed : Visibility.Visible;
        }
        else
        {
            LoginButton.Visibility = Visibility.Visible;
            UserButton.Visibility = Visibility.Collapsed;
        }
    }

    private void OnLoginSucceeded(string username, UserInfo? userInfo)
    {
        _viewModel.UpdateAuthState(username, userInfo);
        _loginSucceeded = true;
    }

    private async void OnLoginClick(object sender, RoutedEventArgs e)
    {
        await ShowLoginDialogAsync();
    }

    private async void OnLogoutClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.LogoutCommand.ExecuteAsync(null);
            _dashboardPage = null;
            _storagePage = null;
            PortalContainer.Child = null;
            StorageContainer.Child = null;
            NavigateTo(AppMode.Landing);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Logout error: {ex}");
        }
    }

    private bool _isHandlingUnauthorized;

    private async void OnUnauthorized(object? sender, EventArgs e)
    {
        if (_isHandlingUnauthorized) return;
        _isHandlingUnauthorized = true;
        try { await _viewModel.HandleTokenExpiredAsync(); }
        finally { _isHandlingUnauthorized = false; }
    }

    private void OnTokenExpired(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            // Invalidate the auth-bound Portal so it rebuilds fresh after the next sign-in.
            _dashboardPage = null;
            PortalContainer.Child = null;
            // Only move the user if they're sitting on the now-dead Portal; anywhere else keep
            // their context — the persistent sign-in bar carries the message and the way back in.
            if (_currentMode == AppMode.Portal) NavigateTo(AppMode.Landing);
            _landingView.StatusMessage = Loc.T("MainWindow_SessionExpired");
            SessionExpiredBar.IsOpen = true;
        });
    }

    private async void OnSessionExpiredSignInClick(object sender, RoutedEventArgs e)
    {
        if (await ShowLoginDialogAsync())
            SessionExpiredBar.IsOpen = false;
    }

    #endregion

    #region Settings

    private async void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        await Views.Notebook.NotebookSettingsDialog.ShowAsync(Content.XamlRoot);
    }

    private async void OnConnectAgentClick(object sender, RoutedEventArgs e)
    {
        await Views.Dialogs.AiConnectWizardDialog.ShowAsync(Content.XamlRoot);
    }

    private bool _wizardOpen;

    // Landing "AI Assistant" tile — same wizard as OnConnectAgentClick, guarded
    // because only one ContentDialog may be open per XamlRoot.
    private async void OnAiAssistantRequested(object? sender, EventArgs e)
    {
        if (_wizardOpen) return;
        _wizardOpen = true;
        try { await Views.Dialogs.AiConnectWizardDialog.ShowAsync(Content.XamlRoot); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Connect wizard error: {ex.Message}"); }
        finally { _wizardOpen = false; }
    }

    private async void OnMcpServerClick(object sender, RoutedEventArgs e)
    {
        await Views.Dialogs.McpServerDialog.ShowAsync(Content.XamlRoot);
    }

    private async void OnOpenSettingsClick(object sender, RoutedEventArgs e)
    {
        await Views.Dialogs.SettingsDialog.ShowAsync(Content.XamlRoot, ShowTermsViewerAsync);
    }

    private async void OnImageDiscoveryClick(object sender, RoutedEventArgs e)
    {
        await Views.Dialogs.ImageDiscoverySettingsDialog.ShowAsync(Content.XamlRoot);
    }

    private async void OnAIComputeClick(object sender, RoutedEventArgs e)
    {
        await Views.Dialogs.AIComputeSettingsDialog.ShowAsync(Content.XamlRoot);
    }

    #endregion

    #region Terms of Use

    private static bool UseFrenchTerms()
        => LegalTerms.IsFrench(System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);

    /// <summary>Show the blocking first-launch Terms gate if the current terms are unaccepted.</summary>
    private void ShowTermsGateIfNeeded()
    {
        if (_legal.HasAcceptedCurrent) return;

        var french = UseFrenchTerms();
        TermsTitle.Text = LegalTerms.Title(french);
        TermsBody.Text = LegalTerms.Body(french);
        TermsGateOverlay.Visibility = Visibility.Visible;

        // The overlay is opaque, so hiding the rows underneath changes nothing
        // visually — but it removes their controls from the Tab order. Without
        // this, Tab could reach (and Enter could activate) Login/Settings/tiles
        // behind the gate before the terms were accepted.
        TitleBarRow.Visibility = Visibility.Collapsed;
        ContentRow.Visibility = Visibility.Collapsed;
        DispatcherQueue.TryEnqueue(() => TermsAcceptButton.Focus(FocusState.Programmatic));
    }

    private void OnTermsAccept(object sender, RoutedEventArgs e)
    {
        _legal.Accept();
        TermsGateOverlay.Visibility = Visibility.Collapsed;
        TitleBarRow.Visibility = Visibility.Visible;
        ContentRow.Visibility = Visibility.Visible;
        _ = ShowWelcomeIfNeededAsync();
    }

    private bool _welcomeDialogOpen;

    /// <summary>
    /// First-run Welcome card (macOS WelcomeSheet parity): shown once Terms are accepted and the
    /// current Welcome version hasn't been seen. The constructor-path call runs before the window
    /// content joins the visual tree (XamlRoot is null), so it waits for the first Activated.
    /// The seen-version stamp is written only after a successful presentation, so a collision with
    /// another ContentDialog just defers the card to the next launch.
    /// </summary>
    private async Task ShowWelcomeIfNeededAsync()
    {
        if (_welcomeDialogOpen) return;
        if (!_legal.HasAcceptedCurrent) return;
        if (WelcomePreferences.SeenVersion >= WelcomePreferences.CurrentVersion) return;

        if (Content?.XamlRoot is null)
        {
            var tcs = new TaskCompletionSource();
            void OnActivatedOnce(object s, WindowActivatedEventArgs e) { Activated -= OnActivatedOnce; tcs.TrySetResult(); }
            Activated += OnActivatedOnce;
            await tcs.Task;
            if (Content?.XamlRoot is null) return;
        }

        bool setUpAssistant;
        _welcomeDialogOpen = true;
        try { setUpAssistant = await WelcomeDialog.ShowAsync(Content.XamlRoot); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Welcome dialog error: {ex.Message}");
            return;
        }
        finally { _welcomeDialogOpen = false; }

        WelcomePreferences.MarkSeen();
        if (setUpAssistant) OnAiAssistantRequested(this, EventArgs.Empty);
    }

    private void OnTermsDecline(object sender, RoutedEventArgs e)
    {
        Application.Current.Exit();
    }

    /// <summary>Dismissible Terms viewer reachable from the About dialog.</summary>
    private async Task ShowTermsViewerAsync()
    {
        var french = UseFrenchTerms();
        var body = new TextBlock
        {
            Text = LegalTerms.Body(french),
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true
        };
        var scroll = new ScrollViewer
        {
            Content = body,
            MaxHeight = 480,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var dialog = new ContentDialog
        {
            Title = LegalTerms.Title(french),
            Content = scroll,
            CloseButtonText = Loc.T("Common_Close"),
            XamlRoot = Content.XamlRoot
        };
        await dialog.ShowAsync();
    }

    #endregion

    /// <summary>Render a figure of the FITS tab on screen. UI-thread work: the plate is a real control.</summary>
    private Task<CanfarDesktop.Mcp.Tools.Write.FitsFigureOutcome> ExportFitsFigureActionAsync(
        CanfarDesktop.Mcp.Tools.Write.FitsFigureRequest request)
        => OnUiAsync(
            () => _fitsTabHost?.ExportFigureAsync(request)
                  ?? Task.FromResult(CanfarDesktop.Mcp.Tools.Write.FitsFigureOutcome.Unavailable("no FITS image is open")),
            CanfarDesktop.Mcp.Tools.Write.FitsFigureOutcome.Unavailable("the window is closing"));

    /// <summary>
    /// What the FITS viewer is showing, for get_fits_image. UI-thread work: the capture rasterises a
    /// real control.
    /// </summary>
    private Task<CanfarDesktop.Mcp.Tools.Write.ViewerCapture> CaptureFitsActionAsync(
        CanfarDesktop.Mcp.Tools.Write.ViewerCaptureRequest request)
        => OnUiAsync(
            () => _fitsTabHost?.CaptureAsync(request)
                  ?? Task.FromResult(CanfarDesktop.Mcp.Tools.Write.ViewerCapture.Unavailable("no FITS image is open")),
            CanfarDesktop.Mcp.Tools.Write.ViewerCapture.Unavailable("the window is closing"));

    /// <summary>What the cube viewer is showing, for get_cube_image.</summary>
    private Task<CanfarDesktop.Mcp.Tools.Write.ViewerCapture> CaptureCubeActionAsync(
        CanfarDesktop.Mcp.Tools.Write.ViewerCaptureRequest request)
        => OnUiAsync(
            () => _cubeTabHost?.ActivePage?.CaptureAsync(request)
                  ?? Task.FromResult(CanfarDesktop.Mcp.Tools.Write.ViewerCapture.Unavailable("no cube is open")),
            CanfarDesktop.Mcp.Tools.Write.ViewerCapture.Unavailable("the window is closing"));

    /// <summary>A notebook cell's figure, for get_cell_image. UI-thread work: it reads the live cell.</summary>
    private Task<CanfarDesktop.Services.Notebook.NotebookCellImage> GetCellImageActionAsync(int index, string? notebook)
        => OnUi(() => _notebookTabHost?.GetCellImage(index, notebook)
                      ?? CanfarDesktop.Services.Notebook.NotebookCellImage.None("no notebook is open"),
                CanfarDesktop.Services.Notebook.NotebookCellImage.None("the window is closing"));

    /// <summary>Close a tab by index, or the active one when no index is given.</summary>
    private Task<TabActionOutcome> CloseTabByIndexActionAsync(string kind, int? index)
        => OnUi(() =>
        {
            if (index is not int i)
            {
                var closed = kind == "fits"
                    ? _fitsTabHost?.CloseActiveTab() == true
                    : _cubeTabHost?.CloseActiveTab() == true;
                return new TabActionOutcome(closed, kind, null,
                    closed ? null : $"no {(kind == "fits" ? "FITS" : "cube")} tab is open");
            }

            return kind switch
            {
                "fits" => Outcome(_fitsTabHost?.CloseTabAt(i) == true, kind, i, "FITS"),
                "cube" => Outcome(_cubeTabHost?.CloseTabAt(i) == true, kind, i, "cube"),
                _ => new TabActionOutcome(false, kind, i, "unknown kind"),
            };
        }, new TabActionOutcome(false, kind, index, "could not dispatch to UI"));

    /// <summary>
    /// Reach the Search page for the <c>search_*</c> tools, creating it if this is the first anyone has
    /// asked. Runs on the UI thread because building the page is XAML work; the tools then call the
    /// bridge, which marshals each of its own operations.
    ///
    /// The page is NOT brought to the front here: reading the form should not yank the user off what
    /// they are looking at. The tools that change something do the navigating.
    /// </summary>
    private Task<CanfarDesktop.Mcp.Tools.Write.ISearchUiBridge?> ResolveSearchBridgeAsync()
        => OnUi<CanfarDesktop.Mcp.Tools.Write.ISearchUiBridge?>(() =>
        {
            EnsureSearchPage();
            return _searchPage;
        }, null);


    /// <summary>
    /// The file the named viewer is showing. Null when it has nothing open — which the annotation tools
    /// read as "nothing is open", an answer with a way round it, since every one of them takes a target.
    /// </summary>
    Task<string?> CanfarDesktop.Mcp.Tools.Write.IAnnotationHost.ActiveTargetAsync(
        CanfarDesktop.Mcp.Tools.Write.AnnotationViewer viewer)
        => OnUi<string?>(() => viewer == CanfarDesktop.Mcp.Tools.Write.AnnotationViewer.Cube
            ? _cubeTabHost?.ActivePage?.AnnotationTarget
            : _fitsTabHost?.ActiveAnnotationTarget, null);

    /// <summary>
    /// Redraw a viewer's marks, optionally picking one out. False when it is not showing that file: the
    /// marks are stored against the file, so they will be there when it is opened.
    /// </summary>
    Task<bool> CanfarDesktop.Mcp.Tools.Write.IAnnotationHost.RefreshAsync(
        CanfarDesktop.Mcp.Tools.Write.AnnotationViewer viewer, string target, string? selectId)
        => OnUi(() => viewer == CanfarDesktop.Mcp.Tools.Write.AnnotationViewer.Cube
            ? _cubeTabHost?.ActivePage?.RefreshAnnotations(target, selectId) ?? false
            : _fitsTabHost?.RefreshAnnotations(target, selectId) ?? false, false);

    Task<bool> CanfarDesktop.Mcp.Tools.Write.IAnnotationHost.DeselectAsync(
        CanfarDesktop.Mcp.Tools.Write.AnnotationViewer viewer, string target)
        => OnUi(() => viewer == CanfarDesktop.Mcp.Tools.Write.AnnotationViewer.Cube
            ? _cubeTabHost?.ActivePage?.DeselectAnnotation(target) ?? false
            : _fitsTabHost?.DeselectAnnotation(target) ?? false, false);

    /// <summary>
    /// Write a file's marks out as JSON or a DS9 region file.
    ///
    /// <para>Everything the document needs is in three places and none of them is the marks: the
    /// STORE has the marks, the VIEWER has the image they are on, and the observation store knows
    /// where that image came from. Gathered here because this is the only object that can see all
    /// three; the shaping is <see cref="Helpers.MarkExport"/>, which is pure and tested.</para>
    /// </summary>
    private Task<CanfarDesktop.Mcp.Tools.Write.AnnotationExportOutcome> ExportAnnotationsActionAsync(
        CanfarDesktop.Mcp.Tools.Write.AnnotationExportRequest request)
        => OnUiAsync(async () =>
        {
            var cube = string.Equals(request.Viewer, "cube", StringComparison.OrdinalIgnoreCase);

            // Resolved the way the annotation tools resolve it, so "this file" means the extension on
            // screen here too — an export of the chip the person is not looking at would describe
            // marks they cannot see.
            var target = Helpers.MarkTarget.Resolve(
                request.Target, null,
                cube ? _cubeTabHost?.ActivePage?.Target : _fitsTabHost?.ActiveAnnotationTarget,
                perExtension: !cube);

            if (string.IsNullOrWhiteSpace(target))
                return new CanfarDesktop.Mcp.Tools.Write.AnnotationExportOutcome(
                    false, request.Path, null, 0, $"nothing is open in the {request.Viewer} viewer");

            var store = App.Services.GetRequiredService<CanfarDesktop.Services.Fits.IAnnotationStore>();
            var marks = store.LoadFor(target);

            // The image the marks are on. A cube has no WCS of the flat kind, so it exports its marks
            // with positions and without a sky — which is what a voxel is.
            var source = cube
                ? new Helpers.MarkExport.Source(target, System.IO.Path.GetFileName(target), 0, null, 0, 0, null)
                : _fitsTabHost?.MarkExportSource(target);

            if (source is null)
                return new CanfarDesktop.Mcp.Tools.Write.AnnotationExportOutcome(
                    false, request.Path, null, 0, "that file is not the one on screen, so its image is not loaded");

            var document = Helpers.MarkExport.Build(
                marks, source, ProvenanceFor(Helpers.MarkTarget.PathOf(target)), App.AppVersion(), DateTime.UtcNow);

            var extension = System.IO.Path.GetExtension(request.Path).ToLowerInvariant();
            var text = extension == ".reg"
                ? Helpers.Ds9Regions.Write(document)
                : System.Text.Json.JsonSerializer.Serialize(document, MarkExportJson);

            try
            {
                await Helpers.AtomicFile.WriteAllTextAsync(request.Path, text);
            }
            catch (Exception ex)
            {
                return new CanfarDesktop.Mcp.Tools.Write.AnnotationExportOutcome(
                    false, request.Path, extension.TrimStart('.'), 0, ex.Message);
            }

            return new CanfarDesktop.Mcp.Tools.Write.AnnotationExportOutcome(
                true, request.Path, extension.TrimStart('.'), document.Marks.Count, null);
        }, new CanfarDesktop.Mcp.Tools.Write.AnnotationExportOutcome(
            false, request.Path, null, 0, "could not dispatch to UI"));

    // ── Pointing the person at a control ────────────────────────────────────────────────────────

    /// <summary>
    /// Show somebody where a control is.
    ///
    /// <para>Resolved against the tree as it is at this moment, on the UI thread, because "what is on
    /// screen" is the whole question — a name that matched a minute ago may be on a page that has
    /// since been navigated away from.</para>
    /// </summary>
    private Task<CanfarDesktop.Mcp.Tools.Write.UiPointOutcome> PointAtUiActionAsync(
        CanfarDesktop.Mcp.Tools.Write.UiPointRequest request)
        => OnUi(() =>
        {
            var (root, host, _) = PointerScope();
            var targets = Views.Controls.AgentPointer.Targets(root);

            if (targets.Count == 0)
                return new CanfarDesktop.Mcp.Tools.Write.UiPointOutcome(
                    false, request.Target, "nothing is on screen to point at yet");

            var id = Helpers.UiPointer.Best(targets, request.Target);
            var element = id is null ? null : Views.Controls.AgentPointer.Find(root, id);

            // Not among what is showing — but it may be on this page, folded inside a closed section.
            // Those are opened to look, and closed again if the name still lands on nothing.
            element ??= Views.Controls.AgentPointer.WithCollapsedOpen(root, () =>
            {
                var widened = Views.Controls.AgentPointer.Targets(root);
                if (Helpers.UiPointer.Best(widened, request.Target) is not { } hidden) return null;
                id = hidden;
                return Views.Controls.AgentPointer.Find(root, hidden);
            });

            // Nothing, or two things equally: either way the caller gets the list rather than a guess.
            if (element is null || id is null)
                return new CanfarDesktop.Mcp.Tools.Write.UiPointOutcome(
                    false, request.Target,
                    $"no single control on screen matches \"{request.Target}\" — these are here now",
                    Describe(Helpers.UiPointer.Suggest(targets, request.Target)));

            var showing = Views.Controls.AgentPointer.Show(
                host, element, request.Title, request.Message,
                request.UntilClosed ? null : Helpers.UiPointer.Seconds(request.Seconds));

            return new CanfarDesktop.Mcp.Tools.Write.UiPointOutcome(
                true, id, showing > 1 ? $"{showing} hints are up" : null);
        }, new CanfarDesktop.Mcp.Tools.Write.UiPointOutcome(false, request.Target, "could not dispatch to UI"));

    private Task<CanfarDesktop.Mcp.Tools.Write.UiTargetListing> ListUiTargetsActionAsync(
        string? contains, bool includeCollapsed)
        => OnUi(() =>
        {
            var (root, _, dialog) = PointerScope();

            // Read before anything is opened, so the listing reports the page as the person sees it.
            var collapsed = Views.Controls.AgentPointer.CollapsedSections(root);

            var targets = includeCollapsed
                ? Views.Controls.AgentPointer.TargetsIncludingCollapsed(root)
                : Views.Controls.AgentPointer.Targets(root);

            if (!string.IsNullOrWhiteSpace(contains))
            {
                var wanted = Helpers.UiPointer.Normalise(contains);
                targets = targets
                    .Where(t => Helpers.UiPointer.Normalise(t.Id).Contains(wanted)
                             || Helpers.UiPointer.Normalise(t.Label).Contains(wanted))
                    .ToList();
            }

            return new CanfarDesktop.Mcp.Tools.Write.UiTargetListing(Describe(targets), Describe(collapsed), dialog);
        }, new CanfarDesktop.Mcp.Tools.Write.UiTargetListing([], []));

    /// <summary>
    /// Where the person can look and click right now: an open dialog when there is one — the window
    /// behind it is under its smoke layer — otherwise the window. A hint about a control in a dialog is
    /// hosted in the dialog when it has a host, so it sits above it and closes with it.
    /// </summary>
    private (DependencyObject Root, Panel Host, string? Dialog) PointerScope()
        => Views.Controls.AgentPointer.OpenDialog(Content.XamlRoot) is { } dialog
            ? (dialog, dialog.FindName("AgentPointerHost") as Panel ?? AgentPointerHost, dialog.Title as string ?? "a dialog")
            : (Content, AgentPointerHost, null);

    // ── Settings, for an agent to show the person ──

    private Task<CanfarDesktop.Mcp.Tools.Write.SettingsShown> OpenSettingsActionAsync(string? section)
        => OnUiAsync(async () =>
        {
            if (Views.Dialogs.SettingsDialog.Current is { } open)
            {
                if (section is not null) open.ShowSection(section);
                return new CanfarDesktop.Mcp.Tools.Write.SettingsShown(true, open.Section);
            }

            // One dialog at a time is WinUI's rule, and the one open is the person's business.
            if (Views.Controls.AgentPointer.OpenDialog(Content.XamlRoot) is { } other)
                return new CanfarDesktop.Mcp.Tools.Write.SettingsShown(false, null,
                    $"another dialog is open (\"{other.Title as string ?? "a dialog"}\") — only one can be open " +
                    "at a time, so the person needs to close it first");

            var dialog = await Views.Dialogs.SettingsDialog.OpenAsync(Content.XamlRoot, ShowTermsViewerAsync, section ?? "general");
            return new CanfarDesktop.Mcp.Tools.Write.SettingsShown(true, dialog.Section);
        }, new CanfarDesktop.Mcp.Tools.Write.SettingsShown(false, section, "could not dispatch to UI"));

    private Task<CanfarDesktop.Mcp.Tools.Write.SettingsShown> CloseSettingsActionAsync()
        => OnUi(() =>
        {
            if (Views.Dialogs.SettingsDialog.Current is not { } open)
                return new CanfarDesktop.Mcp.Tools.Write.SettingsShown(false, null, "Settings was not open");

            open.Hide();
            return new CanfarDesktop.Mcp.Tools.Write.SettingsShown(false, null);
        }, new CanfarDesktop.Mcp.Tools.Write.SettingsShown(false, null, "could not dispatch to UI"));

    private static IReadOnlyList<CanfarDesktop.Mcp.Tools.Write.UiTarget> Describe(
        IReadOnlyList<Helpers.UiPointer.Target> targets)
        => targets.Select(t => new CanfarDesktop.Mcp.Tools.Write.UiTarget(t.Id, t.Kind, t.Label)).ToList();

    /// <summary>Where a local file came from, when the app downloaded it. Null when it was opened off disk.</summary>
    private static Helpers.MarkExport.Provenance? ProvenanceFor(string localPath)
    {
        var store = App.Services.GetRequiredService<ObservationStore>();

        var obs = store.Observations.FirstOrDefault(o =>
            !string.IsNullOrWhiteSpace(o.LocalPath) &&
            string.Equals(System.IO.Path.GetFullPath(o.LocalPath), System.IO.Path.GetFullPath(localPath),
                          StringComparison.OrdinalIgnoreCase));

        return obs is null ? null : new Helpers.MarkExport.Provenance(
            obs.PublisherID, obs.Collection, obs.ObservationID, obs.TargetName, obs.Instrument, obs.Filter,
            obs.StartDate, obs.CalLevel, obs.DataRelease,
            obs.ProposalId, obs.ProposalPi, obs.ProposalTitle,
            IsoTime.OfUtc(obs.DownloadedAt),
            obs.PreviewURL, obs.ThumbnailURL);
    }

    /// <summary>Indented and camel-cased: this file is meant to be read by a person as well as a script.</summary>
    private static readonly System.Text.Json.JsonSerializerOptions MarkExportJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>One shape for "did that index exist", so the close paths refuse the same way.</summary>
    private static TabActionOutcome Outcome(bool ok, string kind, int index, string label)
        => new(ok, kind, index, ok ? null : $"there is no {label} tab {index}");

}
