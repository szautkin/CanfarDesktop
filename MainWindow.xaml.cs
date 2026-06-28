using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Services;
using CanfarDesktop.Services.HttpClients;
using CanfarDesktop.Services.Notebook;
using CanfarDesktop.ViewModels;
using CanfarDesktop.Views;
using CanfarDesktop.Views.Dialogs;
using CanfarDesktop.Views.Notebook;
using static CanfarDesktop.Views.WindowHelper;

namespace CanfarDesktop;

public sealed partial class MainWindow : Window
{
    private enum AppMode { Landing, Portal, Search, Research, Storage, Notebook, FitsViewer, ObservationDetail, CubeViewer }

    private readonly MainViewModel _viewModel;
    private readonly ILegalAgreementService _legal;
    private readonly LandingView _landingView;
    private DashboardPage? _dashboardPage;
    private SearchPage? _searchPage;
    private ResearchPage? _researchPage;
    private StorageBrowserPage? _storagePage;
    private ObservationDetailPage? _obsDetailPage;
    private LocalFileBrowserPanel? _filePanel;
    private bool _filePanelVisible;
    private AppMode _currentMode = AppMode.Landing;
    private readonly Stack<AppMode> _navigationStack = new();
    private bool _loginSucceeded;

    public MainWindow()
    {
        InitializeComponent();
        TrackWindow(this);

        // Window setup
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
        appWindow.SetIcon("Assets/Verbinal.ico");
        appWindow.Title = "Verbinal - a CANFAR Science Portal";

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
        LandingContainer.Child = _landingView;

        Activated += OnWindowActivated;

        InitViewStateTracking();

        ShowTermsGateIfNeeded();
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
                                      GetKernelStateActionAsync, ListNotebooksActionAsync);
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
                ? $"Agent is working — {TitleForModule(module)}"
                : "Agent is working…";
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
        "search" => "Search",
        "portal" => "Portal",
        "storage" => "Storage",
        "research" => "Research",
        "fitsViewer" => "FITS Viewer",
        "notebook" => "Notebook",
        _ => "the app",
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
            AuthProgress.IsActive = true;
            await _viewModel.InitializeAsync();
            AuthProgress.IsActive = false;
            UpdateAuthUI();
            _landingView.StatusMessage = _viewModel.StatusMessage;
            // Stay on Landing — user chooses where to go
        }
        catch (Exception ex)
        {
            AuthProgress.IsActive = false;
            StatusText.Text = $"Startup error: {ex.Message}";
        }
    }

    #endregion

    #region Navigation

    private void NavigateTo(AppMode mode)
    {
        if (_currentMode != mode)
            _navigationStack.Push(_currentMode);

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

        BackButton.Visibility = _navigationStack.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        PublishViewMode();
    }

    private void OnToggleFilePanel(object sender, RoutedEventArgs e) => ToggleFilePanel();

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (_navigationStack.Count > 0)
        {
            var previous = _navigationStack.Pop();
            _currentMode = previous; // set directly to avoid re-pushing
            LandingContainer.Visibility = previous == AppMode.Landing ? Visibility.Visible : Visibility.Collapsed;
            PortalContainer.Visibility = previous == AppMode.Portal ? Visibility.Visible : Visibility.Collapsed;
            SearchContainer.Visibility = previous == AppMode.Search ? Visibility.Visible : Visibility.Collapsed;
            ResearchContainer.Visibility = previous == AppMode.Research ? Visibility.Visible : Visibility.Collapsed;
            StorageContainer.Visibility = previous == AppMode.Storage ? Visibility.Visible : Visibility.Collapsed;
            NotebookContainer.Visibility = previous == AppMode.Notebook ? Visibility.Visible : Visibility.Collapsed;
            FitsViewerContainer.Visibility = previous == AppMode.FitsViewer ? Visibility.Visible : Visibility.Collapsed;
            ObsDetailContainer.Visibility = previous == AppMode.ObservationDetail ? Visibility.Visible : Visibility.Collapsed;
            CubeViewerContainer.Visibility = previous == AppMode.CubeViewer ? Visibility.Visible : Visibility.Collapsed;

            BackButton.Visibility = _navigationStack.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            PublishViewMode();
        }
    }

    private void OnHomeClick(object sender, RoutedEventArgs e) => GoHome();

    private void GoHome()
    {
        _navigationStack.Clear();
        _currentMode = AppMode.Landing; // set directly to avoid re-pushing
        LandingContainer.Visibility = Visibility.Visible;
        PortalContainer.Visibility = Visibility.Collapsed;
        SearchContainer.Visibility = Visibility.Collapsed;
        ResearchContainer.Visibility = Visibility.Collapsed;
        StorageContainer.Visibility = Visibility.Collapsed;
        NotebookContainer.Visibility = Visibility.Collapsed;
        FitsViewerContainer.Visibility = Visibility.Collapsed;
        ObsDetailContainer.Visibility = Visibility.Collapsed;
        CubeViewerContainer.Visibility = Visibility.Collapsed;
        BackButton.Visibility = Visibility.Collapsed;
        PublishViewMode();
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
        catch (Exception ex) { StatusText.Text = $"Notebook error: {ex.Message}"; }
    }

    /// <summary>Ensure the notebook host exists, open <paramref name="filePath"/> (or a new tab if
    /// <paramref name="createNew"/>), switch to the notebook module, and return the active notebook view model.</summary>
    private async Task<ViewModels.Notebook.NotebookViewModel?> OpenNotebookCoreAsync(string? filePath, bool createNew)
    {
        if (_notebookTabHost is null)
        {
            var hostVm = App.Services.GetRequiredService<ViewModels.Notebook.NotebookTabHostViewModel>();
            _notebookTabHost = new NotebookTabHost(hostVm);
            _notebookTabHost.AllTabsClosed += () => NavigateTo(AppMode.Landing);
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

    private async Task<NotebookState?> ApplyNotebookCommandAsync(NotebookCommand cmd)
    {
        // Serialize: pipelined MCP calls enqueue separate lambdas that would otherwise interleave across
        // the long awaits below (run/save/kernel) and mutate the active tab's index/selection mid-flight.
        await _notebookMutateGate.WaitAsync();
        try
        {
            if (cmd.Op == NotebookOp.Open)
            {
                var ov = await OpenNotebookCoreAsync(cmd.Path, createNew: false);
                if (ov is null) throw new InvalidOperationException($"Could not open notebook: {cmd.Path}");
                return ToNotebookState(ov);
            }
            if (cmd.Op == NotebookOp.Create)
                return await OpenNotebookCoreAsync(null, createNew: true) is { } cv ? ToNotebookState(cv) : null;

            var vm = _notebookTabHost?.ViewModel.ActiveViewModel;
            if (vm is null) return null;

            switch (cmd.Op)
            {
                case NotebookOp.Save:
                    if (cmd.Path is { Length: > 0 } sp) await vm.SaveAsAsync(sp);
                    else if (vm.FilePath is null)
                        throw new InvalidOperationException(
                            "Cannot save an unsaved (Untitled) notebook without a path — pass a full .ipynb path to save_notebook.");
                    else await vm.SaveAsync();
                    break;
                case NotebookOp.EditCell:
                    if (InRange(vm, cmd.Index) && cmd.Source is not null)
                        vm.Cells[cmd.Index!.Value].Source = cmd.Source;
                    break;
                case NotebookOp.AddCell:
                    AddNotebookCell(vm, cmd.Index, cmd.CellType ?? "code", cmd.Source);
                    break;
                case NotebookOp.DeleteCell:
                    if (InRange(vm, cmd.Index)) { vm.SelectCell(cmd.Index!.Value); vm.DeleteSelectedCell(); }
                    break;
                case NotebookOp.ChangeCellType:
                    if (InRange(vm, cmd.Index) && cmd.CellType is not null) { vm.SelectCell(cmd.Index!.Value); vm.ChangeCellType(cmd.CellType); }
                    break;
                case NotebookOp.MoveCell:
                    if (!MoveNotebookCell(vm, cmd.Index ?? -1, cmd.ToIndex ?? -1))
                        throw new InvalidOperationException($"move_cell: from/to out of range (notebook has {vm.Cells.Count} cells).");
                    break;
                case NotebookOp.ClearOutputs:
                    vm.ClearAllOutputs();
                    break;
                case NotebookOp.RunCell:
                    if (InRange(vm, cmd.Index)) { vm.SelectCell(cmd.Index!.Value); await vm.RunSelectedCellAsync(); }
                    break;
                case NotebookOp.RunAll:
                    await vm.RunAllCellsAsync();
                    break;
                case NotebookOp.StartKernel:
                    await vm.StartKernelAsync();
                    break;
                case NotebookOp.InterruptKernel:
                    await vm.InterruptKernelAsync();
                    break;
                case NotebookOp.RestartKernel:
                    await vm.RestartKernelAsync();
                    break;
            }

            // The tab may have been closed/swapped mid-await (run/save/kernel ops yield the UI pump) —
            // don't report state from an orphaned VM that is no longer the active tab.
            var stillActive = _notebookTabHost?.ViewModel.ActiveViewModel;
            return ReferenceEquals(stillActive, vm) ? ToNotebookState(vm) : null;
        }
        finally
        {
            _notebookMutateGate.Release();
        }
    }

    private static bool InRange(ViewModels.Notebook.NotebookViewModel vm, int? index)
        => index is { } i && i >= 0 && i < vm.Cells.Count;

    private static void AddNotebookCell(ViewModels.Notebook.NotebookViewModel vm, int? index, string type, string? source)
    {
        if (index is { } i && i >= 0 && i < vm.Cells.Count)
        {
            vm.SelectCell(i);
            vm.AddCellAbove(type); // inserts at i, selects the new cell
        }
        else
        {
            if (vm.Cells.Count > 0) vm.SelectCell(vm.Cells.Count - 1);
            vm.AddCellBelow(type); // appends after the last cell, selects it
        }
        if (source is not null && vm.SelectedCellIndex >= 0 && vm.SelectedCellIndex < vm.Cells.Count)
            vm.Cells[vm.SelectedCellIndex].Source = source;
    }

    private static bool MoveNotebookCell(ViewModels.Notebook.NotebookViewModel vm, int from, int to)
    {
        int n = vm.Cells.Count;
        if (from < 0 || from >= n || to < 0 || to >= n) return false; // out of range → surface as an error
        if (from == to) return true;                                  // legitimate no-op
        vm.SelectCell(from);
        if (to < from) { for (int i = from; i > to; i--) vm.MoveCellUp(); }
        else { for (int i = from; i < to; i++) vm.MoveCellDown(); }
        return true;
    }

    private Task<NotebookState?> GetNotebookActionAsync()
        => OnUi(() => _notebookTabHost?.ViewModel.ActiveViewModel is { } vm ? ToNotebookState(vm) : null, null);

    private Task<NotebookCellOutputs?> GetCellOutputActionAsync(int index)
        => OnUi(() => GetCellOutputsCore(index), null);

    private Task<NotebookKernelInfo> GetKernelStateActionAsync()
        => OnUi(() =>
        {
            var vm = _notebookTabHost?.ViewModel.ActiveViewModel;
            return vm is null
                ? new NotebookKernelInfo("Dead", "no notebook open", "")
                : new NotebookKernelInfo(vm.KernelState.ToString(), vm.KernelStatusText, vm.KernelDisplayName);
        }, new NotebookKernelInfo("Dead", "could not dispatch", ""));

    private Task<IReadOnlyList<NotebookRef>> ListNotebooksActionAsync()
    {
        // RecentNotebooksService is a thread-safe singleton — no UI-thread marshaling needed.
        var recent = App.Services.GetRequiredService<RecentNotebooksService>();
        IReadOnlyList<NotebookRef> list = recent.Entries
            .Select(e => new NotebookRef(e.Path, e.Name, e.OpenedAt)).ToList();
        return Task.FromResult(list);
    }

    private NotebookCellOutputs? GetCellOutputsCore(int index)
    {
        var vm = _notebookTabHost?.ViewModel.ActiveViewModel;
        if (vm is null || index < 0 || index >= vm.Cells.Count) return null;
        var cell = vm.Cells[index];
        if (cell is not ViewModels.Notebook.CodeCellViewModel code)
            return new NotebookCellOutputs(index, cell.CellType, null, Array.Empty<NotebookOutputInfo>());

        const int cap = 16000;
        var outs = new List<NotebookOutputInfo>(code.Outputs.Count);
        foreach (var o in code.Outputs)
            outs.Add(new NotebookOutputInfo(
                o.OutputType, Clip(o.TextContent, cap), o.IsError, o.ErrorName, Clip(o.Traceback, cap), o.HasImage, o.HasHtml));
        return new NotebookCellOutputs(index, "code", code.ExecutionCount, outs);
    }

    private static NotebookState ToNotebookState(ViewModels.Notebook.NotebookViewModel vm)
    {
        const int cap = 16000;
        var cells = new List<NotebookCellInfo>(vm.Cells.Count);
        for (int i = 0; i < vm.Cells.Count; i++)
        {
            var c = vm.Cells[i];
            var src = c.Source ?? string.Empty;
            int? exec = c is ViewModels.Notebook.CodeCellViewModel code ? code.ExecutionCount : null;
            int outs = c is ViewModels.Notebook.CodeCellViewModel cc ? cc.Outputs.Count : 0;
            cells.Add(new NotebookCellInfo(i, c.CellType, Clip(src, cap), src.Length > cap, exec, outs));
        }
        return new NotebookState(
            Loaded: true, Title: vm.Title, FilePath: vm.FilePath, FileMode: vm.FileMode.ToString(),
            IsDirty: vm.IsDirty, KernelState: vm.KernelState.ToString(), KernelName: vm.KernelDisplayName,
            SelectedIndex: vm.SelectedCellIndex, CellCount: vm.Cells.Count, Cells: cells);
    }

    private static string Clip(string? s, int max)
    {
        s ??= string.Empty;
        return s.Length <= max ? s : s[..max];
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
            StatusText.Text = $"Cube viewer error: {ex.Message}";
        }
    }

    public async void OpenFitsViewer(string? filePath = null)
    {
        try { await OpenFitsViewerAsync(filePath); }
        catch (Exception ex) { StatusText.Text = $"FITS viewer error: {ex.Message}"; }
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
            _fitsTabHost.AllTabsClosed += () => NavigateTo(AppMode.Landing);
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

    /// <summary>Show login dialog. Returns true if login succeeded.</summary>
    private async Task<bool> ShowLoginDialogAsync()
    {
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
            StatusText.Text = $"Login error: {ex.Message}";
            return false;
        }
        finally
        {
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
            if (e.PropertyName is "IsAuthenticated" or "Username" or "StatusMessage")
            {
                UpdateAuthUI();
                _landingView.StatusMessage = _viewModel.StatusMessage;
            }
        });
    }

    private void UpdateAuthUI()
    {
        StatusText.Text = _viewModel.StatusMessage;

        if (_viewModel.IsAuthenticated)
        {
            LoginButton.Visibility = Visibility.Collapsed;
            UserButton.Visibility = Visibility.Visible;
            UserButton.Content = _viewModel.Username;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
                UserButton, $"{_viewModel.Username} — account options");
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
            _landingView.StatusMessage = "Session expired. Please log in again.";
        });
    }

    #endregion

    #region Settings

    private async void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        await Views.Notebook.NotebookSettingsDialog.ShowAsync(Content.XamlRoot);
    }

    private async void OnMcpServerClick(object sender, RoutedEventArgs e)
    {
        await Views.Dialogs.McpServerDialog.ShowAsync(Content.XamlRoot);
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
            Text = "A CANFAR Science Portal Companion",
            Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        };

        var version = new TextBlock
        {
            Text = $"Version {GetAppVersion()}",
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
            Content = "Visit canfar.net",
            NavigateUri = new Uri("https://www.canfar.net"),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var termsLink = new HyperlinkButton
        {
            Content = "Terms of Use",
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
            Title = "About Verbinal",
            Content = panel,
            CloseButtonText = "Close",
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
    }

    private void OnTermsAccept(object sender, RoutedEventArgs e)
    {
        _legal.Accept();
        TermsGateOverlay.Visibility = Visibility.Collapsed;
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
            CloseButtonText = "Close",
            XamlRoot = Content.XamlRoot
        };
        await dialog.ShowAsync();
    }

    #endregion
}
