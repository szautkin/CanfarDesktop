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

public sealed partial class MainWindow : Window
{
    private enum AppMode { Landing, Portal, Search, Research, Storage, Notebook, FitsViewer, ObservationDetail, CubeViewer, AiGuide, Workflows }

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
        _landingView.PortalRequested += OnPortalRequested;
        _landingView.SearchRequested += OnSearchRequested;
        _landingView.ResearchRequested += OnResearchRequested;
        _landingView.StorageRequested += (_, _) => OpenStorageBrowser();
        _landingView.NotebookRequested += (_, _) => OpenNotebook();
        _landingView.FitsViewerRequested += (_, _) => OpenFitsViewer();
        _landingView.CubeViewerRequested += (_, _) => OpenCubeViewer();
        _landingView.AiGuideRequested += (_, _) => OpenAiGuidePage();
        _landingView.WorkflowsRequested += (_, _) => OpenWorkflowsPage();
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
                                  ExportCubeActionAsync, ProbeCubeActionAsync);
        _viewState.SetFitsActions(GetFitsActionAsync, SetFitsActionAsync, ProbeFitsActionAsync, GotoFitsActionAsync);
        _viewState.SetFitsBookmarkActions(ListFitsBookmarksActionAsync, SaveFitsBookmarkActionAsync, DeleteFitsBookmarkActionAsync);
        _viewState.SetNotebookActions(NotebookMutateActionAsync, GetNotebookActionAsync, GetCellOutputActionAsync,
                                      GetKernelStateActionAsync, ListNotebooksActionAsync, ListOpenNotebooksActionAsync);
        _viewState.SetTabActions(CloseTabActionAsync, ListOpenTabsActionAsync);
        _viewState.SetCreateAnalysisNotebookAction(CreateAnalysisNotebookActionAsync);
        _viewState.AgentActivity += OnAgentActivity;
        PublishViewMode();
    }

    // ── Live ViewState write actions (invoked off-thread by the MCP tools; marshal to the UI thread) ──

    // The MCP action methods all share one shape: run `work` on the UI thread, complete a
    // TaskCompletionSource (faulting it if `work` throws), and return `fallback` if the dispatch itself
    // can't be queued. These two helpers collapse that boilerplate to a single expression per action.
    private Task<T> OnUi<T>(Func<T> work, T fallback)
    {
        var tcs = new TaskCompletionSource<T>();
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            try { tcs.SetResult(work()); }
            catch (Exception ex) { tcs.SetException(ex); }
        }))
            tcs.SetResult(fallback);
        return tcs.Task;
    }

    private Task<T> OnUiAsync<T>(Func<Task<T>> work, T fallback)
    {
        var tcs = new TaskCompletionSource<T>();
        // try/catch INSIDE the enqueued async lambda (await inside, not around the TCS) so exception +
        // ordering behaviour matches the hand-written versions.
        if (!DispatcherQueue.TryEnqueue(async () =>
        {
            try { tcs.SetResult(await work()); }
            catch (Exception ex) { tcs.SetException(ex); }
        }))
            tcs.SetResult(fallback);
        return tcs.Task;
    }

    private Task<CanfarDesktop.Mcp.Tools.Write.NavigationOutcome> NavigateByKeyAsync(string mode)
        => OnUi(() => NavigateByKey(mode), new CanfarDesktop.Mcp.Tools.Write.NavigationOutcome(false, mode, mode));

    private CanfarDesktop.Mcp.Tools.Write.NavigationOutcome NavigateByKey(string mode)
    {
        switch (mode)
        {
            case "landing": GoHome(); return new(true, "landing", "Home");
            case "portal": EnsureDashboard(); NavigateTo(AppMode.Portal); return new(true, "portal", "Portal");
            case "search": EnsureSearchPage(); NavigateTo(AppMode.Search); return new(true, "search", "Search");
            case "research": EnsureResearchPage(); NavigateTo(AppMode.Research); return new(true, "research", "Research");
            case "storage": OpenStorageBrowser(); return new(true, "storage", "Storage");
            case "notebook": OpenNotebook(); return new(true, "notebook", "Notebook");
            case "fitsViewer": NavigateTo(AppMode.FitsViewer); return new(true, "fitsViewer", "FITS Viewer");
            case "aiGuide": OpenAiGuidePage(); return new(true, "aiGuide", "AI Guide");
            case "workflows": OpenWorkflowsPage(); return new(true, "workflows", "Workflows");
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
            var store = App.Services.GetRequiredService<ObservationStore>();
            var obs = store.Observations.FirstOrDefault(o => o.Id == id || o.PublisherID == id);
            if (obs is null)
                return new CanfarDesktop.Mcp.Tools.Write.OpenFitsOutcome(false, id, null, "observation not found in Research");
            if (!obs.FileExists)
                return new(false, id, obs.LocalPath, "not downloaded yet — use download_observation first");
            // Await the actual parse and report opened:true only on a confirmed load — so a file that won't
            // parse (e.g. a non-FITS download) returns the real error, not optimism.
            var page = await OpenFitsViewerAsync(obs.LocalPath);
            var error = page?.ViewModel.LoadError;
            return error is null ? new(true, obs.Id, obs.LocalPath, null) : new(false, obs.Id, obs.LocalPath, error);
        }, new CanfarDesktop.Mcp.Tools.Write.OpenFitsOutcome(false, id, null, "could not dispatch to UI"));

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
            var page = await host.AddTabForFileAsync(path);
            var st = page.GetCubeState();
            return st.Loaded
                ? new(true, path, st.Nx, st.Ny, st.Nz, null)
                : new(false, path, st.Nx, st.Ny, st.Nz, "not a 3D cube (NAXIS=3) or could not be read");
        }, new CanfarDesktop.Mcp.Tools.Write.CubeOpenOutcome(false, target, 0, 0, 0, "could not dispatch to UI"));

    private string? ResolveCubeTarget(string target)
    {
        if (System.IO.File.Exists(target)) return target;
        var store = App.Services.GetRequiredService<ObservationStore>();
        var obs = store.Observations.FirstOrDefault(o => o.Id == target || o.PublisherID == target);
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
            zoomPercent: args.ZoomPercent, northUp: args.NorthUp, reset: args.Reset, clearCrosshair: args.ClearCrosshair), null);

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
                autoOrbit: args.AutoOrbit, playing: args.Playing, resetCamera: args.ResetCamera);
            return cube.GetCubeState();
        }, null);

    private Task<CanfarDesktop.Mcp.Tools.Write.CubeExportOutcome> ExportCubeActionAsync(
        string path, string format, int scale, bool dark)
        => OnUiAsync(async () =>
        {
            var cube = _cubeTabHost?.ActivePage;
            if (cube is null)
                return new CanfarDesktop.Mcp.Tools.Write.CubeExportOutcome(false, path, "the cube viewer is not open (use open_cube first)");
            var err = await cube.ExportCubeToPathAsync(path, format, scale, dark);
            return err is null ? new(true, path, null) : new(false, path, err);
        }, new CanfarDesktop.Mcp.Tools.Write.CubeExportOutcome(false, path, "could not dispatch to UI"));

    private Task<CanfarDesktop.Services.CubeViewer.CubeSpectrumResult?> ProbeCubeActionAsync(int x, int y)
        => OnUi(() => _cubeTabHost?.ActivePage?.ProbeCubeSpectrum(x, y), null);

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
            _cubeTabHost?.OpenTabCount ?? 0),
            new OpenTabsState(0, 0, 0));

    // ── Analysis-notebook hand-off (SCI-10): resolve the downloaded observation, seed an .ipynb, open it ──
    private Task<NotebookState?> CreateAnalysisNotebookActionAsync(string observationId, string template)
        => OnUiAsync<NotebookState?>(async () =>
        {
            var store = App.Services.GetRequiredService<ObservationStore>();
            var obs = store.Observations.FirstOrDefault(o => o.Id == observationId || o.PublisherID == observationId);
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
            AppMode.ObservationDetail => ("observationDetail", "Observation"),
            AppMode.AiGuide => ("aiGuide", "AI Guide"),
            AppMode.Workflows => ("workflows", "Workflows"),
            _ => ("landing", "Home"),
        };
        _viewState?.SetMode(mode, title);
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

    private void OnAgentActivity(CanfarDesktop.Mcp.AppViewStateService.AgentActivitySignal signal)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            AgentActivityText.Text = signal.Module is { } module
                ? Loc.F("MainWindow_AgentWorkingModule", TitleForModule(module))
                : Loc.T("MainWindow_AgentWorking");
            AgentActivityIndicator.Visibility = Visibility.Visible;

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
        "notebook" => Loc.T("Module_Notebook"),
        "workflows" => "Workflows",
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
        _ => LandingContainer,
    };

    /// <summary>Shared visibility swap for forward, back, and home navigation.</summary>
    private void ApplyMode(AppMode mode)
    {
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

    private async void OnPortalRequested(object? sender, EventArgs e)
    {
        if (!_viewModel.IsAuthenticated)
        {
            if (!await ShowLoginDialogAsync()) return;
        }
        EnsureDashboard();
        NavigateTo(AppMode.Portal);
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
        _workflowsPage.NavigateRequested += key => NavigateByKey(key);
        WorkflowsContainer.Child = _workflowsPage;
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

    public async void OpenStorageBrowser()
    {
        if (!_viewModel.IsAuthenticated)
        {
            if (!await ShowLoginDialogAsync()) return;
        }

        if (_storagePage is null)
        {
            _storagePage = App.Services.GetRequiredService<StorageBrowserPage>();
            _storagePage.OpenInFitsViewerRequested += path => OpenFitsViewer(path);
            _storagePage.OpenInCubeViewerRequested += path => OpenCubeViewer(path);
            StorageContainer.Child = _storagePage;
            await _storagePage.LoadAsync(_viewModel.Username);
        }

        NavigateTo(AppMode.Storage);
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
        if (_fitsTabHost is null)
        {
            var hostVm = App.Services.GetRequiredService<FitsTabHostViewModel>();
            _fitsTabHost = new Views.FitsViewer.FitsTabHost(hostVm);
            _fitsTabHost.SearchAtPositionRequested += OnSearchAtFitsPosition;
            // GoHome (not NavigateTo) so the empty host doesn't stay on the back stack.
            _fitsTabHost.AllTabsClosed += GoHome;
            FitsViewerContainer.Child = _fitsTabHost;
        }

        Views.FitsViewer.FitsViewerPage? page = null;
        if (filePath is not null)
        {
            page = await _fitsTabHost.AddTabForFileAsync(filePath);
            // The tab's FilePath is only set during the (async) load — after the CollectionChanged publish
            // already ran with an empty path. Re-publish now so get_current_view.openFitsPaths is correct.
            PublishOpenFits(_fitsTabHost.ViewModel);
        }

        NavigateTo(AppMode.FitsViewer);
        return page;
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
            vm.Target = $"{Models.Fits.WcsInfo.FormatForResolver(ra, dec)}";
            vm.ResolvedRA = ra;
            vm.ResolvedDec = dec;
            vm.ResolverStatus = "From FITS crosshair";
            vm.ResolverService = prevService;
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
            _dashboardPage = null;
            PortalContainer.Child = null;
            NavigateTo(AppMode.Landing);
            _landingView.StatusMessage = Loc.T("MainWindow_SessionExpired");
        });
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
        await Views.Dialogs.SettingsDialog.ShowAsync(Content.XamlRoot);
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

    #region About

    private async void OnAboutClick(object sender, RoutedEventArgs e)
    {
        var logo = new Image
        {
            Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/Verbinal_icon.png")),
            Width = 64, Height = 64, HorizontalAlignment = HorizontalAlignment.Center
        };

        var title = new TextBlock
        {
            Text = "Verbinal",
            Style = (Style)Application.Current.Resources["TitleTextBlockStyle"],
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var subtitle = new TextBlock
        {
            Text = Loc.T("About_Subtitle"),
            Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        };

        var version = new TextBlock
        {
            Text = Loc.F("About_Version", GetAppVersion()),
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
        };

        var copyright = new TextBlock
        {
            Text = "\u00a9 2026 Serhii Zautkin",
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
        };

        var link = new HyperlinkButton
        {
            Content = Loc.T("About_VisitLink"),
            NavigateUri = new Uri("https://www.canfar.net"),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var termsLink = new HyperlinkButton
        {
            Content = Loc.T("About_TermsOfUse"),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var panel = new StackPanel { Spacing = 12, MinWidth = 300 };
        panel.Children.Add(logo);
        panel.Children.Add(title);
        panel.Children.Add(subtitle);
        panel.Children.Add(version);
        panel.Children.Add(copyright);
        panel.Children.Add(link);
        panel.Children.Add(termsLink);

        var dialog = new ContentDialog
        {
            Title = Loc.T("About_Title"),
            Content = panel,
            CloseButtonText = Loc.T("Common_Close"),
            XamlRoot = Content.XamlRoot
        };

        // Only one ContentDialog may be open at a time — close About before showing Terms.
        termsLink.Click += async (_, _) =>
        {
            dialog.Hide();
            await ShowTermsViewerAsync();
        };

        await dialog.ShowAsync();
    }

    private static string GetAppVersion()
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        return version is not null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.0.0";
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
}
