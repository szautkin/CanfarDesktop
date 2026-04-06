using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.Models;
using CanfarDesktop.Services.HttpClients;
using CanfarDesktop.ViewModels;
using CanfarDesktop.Views;
using CanfarDesktop.Views.Dialogs;
using CanfarDesktop.Views.Notebook;
using static CanfarDesktop.Views.WindowHelper;

namespace CanfarDesktop;

public sealed partial class MainWindow : Window
{
    private enum AppMode { Landing, Portal, Search, Research, Storage, Notebook, FitsViewer }

    private readonly MainViewModel _viewModel;
    private readonly LandingView _landingView;
    private DashboardPage? _dashboardPage;
    private SearchPage? _searchPage;
    private ResearchPage? _researchPage;
    private StorageBrowserPage? _storagePage;
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
        LandingContainer.Child = _landingView;

        Activated += OnWindowActivated;
    }

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

        BackButton.Visibility = _navigationStack.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
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

            BackButton.Visibility = _navigationStack.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OnHomeClick(object sender, RoutedEventArgs e)
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
        BackButton.Visibility = Visibility.Collapsed;
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
        SearchContainer.Child = _searchPage;
        _searchPage.LoadAsync();
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

    public async void OpenFitsViewer(string? filePath = null)
    {
        try
        {
            if (_fitsTabHost is null)
            {
                var hostVm = App.Services.GetRequiredService<FitsTabHostViewModel>();
                _fitsTabHost = new Views.FitsViewer.FitsTabHost(hostVm);
                _fitsTabHost.SearchAtPositionRequested += OnSearchAtFitsPosition;
                _fitsTabHost.AllTabsClosed += () => NavigateTo(AppMode.Landing);
                FitsViewerContainer.Child = _fitsTabHost;
            }

            if (filePath is not null)
                await _fitsTabHost.AddTabForFileAsync(filePath);

            NavigateTo(AppMode.FitsViewer);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"FITS viewer error: {ex.Message}";
        }
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

        var panel = new StackPanel { Spacing = 12, MinWidth = 300 };
        panel.Children.Add(logo);
        panel.Children.Add(title);
        panel.Children.Add(subtitle);
        panel.Children.Add(version);
        panel.Children.Add(copyright);
        panel.Children.Add(link);

        var dialog = new ContentDialog
        {
            Title = "About Verbinal",
            Content = panel,
            CloseButtonText = "Close",
            XamlRoot = Content.XamlRoot
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
}
