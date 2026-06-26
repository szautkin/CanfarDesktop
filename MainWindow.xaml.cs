using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Services;
using CanfarDesktop.Services.HttpClients;
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
        var fitsHost = App.Services.GetRequiredService<FitsTabHostViewModel>();
        fitsHost.Tabs.CollectionChanged += (_, _) => PublishOpenFits(fitsHost);
        _viewState.SetActions(NavigateByKeyAsync, SetSearchFocusActionAsync, OpenFitsActionAsync);
        _viewState.SetCubeActions(OpenCubeActionAsync, GetCubeActionAsync, SetCubeActionAsync,
                                  ExportCubeActionAsync, ProbeCubeActionAsync);
        _viewState.AgentActivity += OnAgentActivity;
        PublishViewMode();
    }

    // ── Live ViewState write actions (invoked off-thread by the MCP tools; marshal to the UI thread) ──

    private Task<CanfarDesktop.Mcp.Tools.Write.NavigationOutcome> NavigateByKeyAsync(string mode)
    {
        var tcs = new TaskCompletionSource<CanfarDesktop.Mcp.Tools.Write.NavigationOutcome>();
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            try { tcs.SetResult(NavigateByKey(mode)); }
            catch (Exception ex) { tcs.SetException(ex); }
        }))
            tcs.SetResult(new CanfarDesktop.Mcp.Tools.Write.NavigationOutcome(false, mode, mode));
        return tcs.Task;
    }

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
    {
        var tcs = new TaskCompletionSource<CanfarDesktop.Mcp.Tools.Write.OpenFitsOutcome>();
        if (!DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var store = App.Services.GetRequiredService<ObservationStore>();
                var obs = store.Observations.FirstOrDefault(o => o.Id == id || o.PublisherID == id);
                if (obs is null)
                    tcs.SetResult(new(false, id, null, "observation not found in Research"));
                else if (!obs.FileExists)
                    tcs.SetResult(new(false, id, obs.LocalPath, "not downloaded yet — use download_observation first"));
                else
                {
                    // Await the actual parse and report opened:true only on a confirmed load — so a file
                    // that won't parse (e.g. a non-FITS download) returns the real error, not optimism.
                    var page = await OpenFitsViewerAsync(obs.LocalPath);
                    var error = page?.ViewModel.LoadError;
                    tcs.SetResult(error is null
                        ? new(true, obs.Id, obs.LocalPath, null)
                        : new(false, obs.Id, obs.LocalPath, error));
                }
            }
            catch (Exception ex) { tcs.SetException(ex); }
        }))
            tcs.SetResult(new(false, id, null, "could not dispatch to UI"));
        return tcs.Task;
    }

    // ── Cube Viewer MCP actions (each marshals to the UI thread) ─────────────────────────────────

    private Task<CanfarDesktop.Mcp.Tools.Write.CubeOpenOutcome> OpenCubeActionAsync(string target)
    {
        var tcs = new TaskCompletionSource<CanfarDesktop.Mcp.Tools.Write.CubeOpenOutcome>();
        if (!DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var path = ResolveCubeTarget(target);
                if (path is null)
                {
                    tcs.SetResult(new(false, target, 0, 0, 0,
                        "file not found, or observation not downloaded (use download_observation first)"));
                    return;
                }
                var host = EnsureCubeHost();
                NavigateTo(AppMode.CubeViewer);
                var page = await host.AddTabForFileAsync(path);
                var st = page.GetCubeState();
                tcs.SetResult(st.Loaded
                    ? new(true, path, st.Nx, st.Ny, st.Nz, null)
                    : new(false, path, st.Nx, st.Ny, st.Nz, "not a 3D cube (NAXIS=3) or could not be read"));
            }
            catch (Exception ex) { tcs.SetException(ex); }
        }))
            tcs.SetResult(new(false, target, 0, 0, 0, "could not dispatch to UI"));
        return tcs.Task;
    }

    private string? ResolveCubeTarget(string target)
    {
        if (System.IO.File.Exists(target)) return target;
        var store = App.Services.GetRequiredService<ObservationStore>();
        var obs = store.Observations.FirstOrDefault(o => o.Id == target || o.PublisherID == target);
        return obs is not null && obs.FileExists ? obs.LocalPath : null;
    }

    private Task<CanfarDesktop.Services.CubeViewer.CubeViewState?> GetCubeActionAsync()
    {
        var tcs = new TaskCompletionSource<CanfarDesktop.Services.CubeViewer.CubeViewState?>();
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            try { tcs.SetResult(_cubeTabHost?.ActivePage?.GetCubeState()); }
            catch (Exception ex) { tcs.SetException(ex); }
        }))
            tcs.SetResult(null);
        return tcs.Task;
    }

    private Task<CanfarDesktop.Services.CubeViewer.CubeViewState?> SetCubeActionAsync(
        CanfarDesktop.Mcp.Tools.Write.CubeViewArgs args)
    {
        var tcs = new TaskCompletionSource<CanfarDesktop.Services.CubeViewer.CubeViewState?>();
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                var cube = _cubeTabHost?.ActivePage;
                if (cube is null) { tcs.SetResult(null); return; }
                cube.ApplyCubeView(args.Mode, args.Channel, args.Colormap, args.Stretch,
                    args.RenderMode, args.WindowLo, args.WindowHi);
                tcs.SetResult(cube.GetCubeState());
            }
            catch (Exception ex) { tcs.SetException(ex); }
        }))
            tcs.SetResult(null);
        return tcs.Task;
    }

    private Task<CanfarDesktop.Mcp.Tools.Write.CubeExportOutcome> ExportCubeActionAsync(
        string path, string format, int scale, bool dark)
    {
        var tcs = new TaskCompletionSource<CanfarDesktop.Mcp.Tools.Write.CubeExportOutcome>();
        if (!DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var cube = _cubeTabHost?.ActivePage;
                if (cube is null)
                {
                    tcs.SetResult(new(false, path, "the cube viewer is not open (use open_cube first)"));
                    return;
                }
                var err = await cube.ExportCubeToPathAsync(path, format, scale, dark);
                tcs.SetResult(err is null ? new(true, path, null) : new(false, path, err));
            }
            catch (Exception ex) { tcs.SetException(ex); }
        }))
            tcs.SetResult(new(false, path, "could not dispatch to UI"));
        return tcs.Task;
    }

    private Task<CanfarDesktop.Services.CubeViewer.CubeSpectrumResult?> ProbeCubeActionAsync(int x, int y)
    {
        var tcs = new TaskCompletionSource<CanfarDesktop.Services.CubeViewer.CubeSpectrumResult?>();
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            try { tcs.SetResult(_cubeTabHost?.ActivePage?.ProbeCubeSpectrum(x, y)); }
            catch (Exception ex) { tcs.SetException(ex); }
        }))
            tcs.SetResult(null);
        return tcs.Task;
    }

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
    private NotebookTabHost? _notebookTabHost;

    public async void OpenNotebook(string? filePath = null)
    {
        try
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
                await _notebookTabHost.AddTabForFileAsync(filePath);

            NavigateTo(AppMode.Notebook);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Notebook error: {ex.Message}";
        }
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
