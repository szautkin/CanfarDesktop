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
            // Open with system default app
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
        _currentMode = mode;
        LandingContainer.Visibility = mode == AppMode.Landing ? Visibility.Visible : Visibility.Collapsed;
        PortalContainer.Visibility = mode == AppMode.Portal ? Visibility.Visible : Visibility.Collapsed;
        SearchContainer.Visibility = mode == AppMode.Search ? Visibility.Visible : Visibility.Collapsed;
        ResearchContainer.Visibility = mode == AppMode.Research ? Visibility.Visible : Visibility.Collapsed;
        StorageContainer.Visibility = mode == AppMode.Storage ? Visibility.Visible : Visibility.Collapsed;
        NotebookContainer.Visibility = mode == AppMode.Notebook ? Visibility.Visible : Visibility.Collapsed;
        FitsViewerContainer.Visibility = mode == AppMode.FitsViewer ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnToggleFilePanel(object sender, RoutedEventArgs e) => ToggleFilePanel();

    private void OnHomeClick(object sender, RoutedEventArgs e)
    {
        NavigateTo(AppMode.Landing);
    }

    private async void OnPortalRequested(object? sender, EventArgs e)
    {
        if (_viewModel.IsAuthenticated)
        {
            EnsureDashboard();
            NavigateTo(AppMode.Portal);
        }
        else
        {
            await ShowLoginThenPortalAsync();
        }
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
            await ShowLoginThenPortalAsync();
            if (!_viewModel.IsAuthenticated) return;
        }

        if (_storagePage is null)
        {
            _storagePage = App.Services.GetRequiredService<StorageBrowserPage>();
            StorageContainer.Child = _storagePage;
            await _storagePage.LoadAsync(_viewModel.Username);
        }

        NavigateTo(AppMode.Storage);
    }

    private Views.FitsViewer.FitsViewerPage? _fitsViewerPage;
    private NotebookTabHost? _notebookTabHost;

    public async void OpenNotebook(string? filePath = null)
    {
        if (_notebookTabHost is null)
        {
            var hostVm = App.Services.GetRequiredService<ViewModels.Notebook.NotebookTabHostViewModel>();
            _notebookTabHost = new NotebookTabHost(hostVm);
            _notebookTabHost.AllTabsClosed += () => NavigateTo(AppMode.Landing);
            NotebookContainer.Child = _notebookTabHost;

            // Check for crash recovery on first open
            await _notebookTabHost.CheckRecoveryAsync();
        }

        // Open specific file or show welcome page (user picks New/Open from there)
        if (filePath is not null)
            await _notebookTabHost.AddTabForFileAsync(filePath);

        NavigateTo(AppMode.Notebook);
    }

    public async void OpenFitsViewer(string? filePath = null)
    {
        if (_fitsViewerPage is null)
        {
            var vm = App.Services.GetRequiredService<FitsViewerViewModel>();
            _fitsViewerPage = new Views.FitsViewer.FitsViewerPage(vm);
            FitsViewerContainer.Child = _fitsViewerPage;
        }

        if (filePath is not null)
            await _fitsViewerPage.OpenFileAsync(filePath);

        NavigateTo(AppMode.FitsViewer);
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

    private async Task ShowLoginThenPortalAsync()
    {
        LoginViewModel? loginVm = null;
        try
        {
            loginVm = App.Services.GetRequiredService<LoginViewModel>();
            _loginSucceeded = false;
            loginVm.LoginSucceeded += OnLoginSucceeded;

            var dialog = new LoginDialog(loginVm) { XamlRoot = Content.XamlRoot };
            await dialog.ShowAsync();

            if (_loginSucceeded)
            {
                EnsureDashboard();
                NavigateTo(AppMode.Portal);
            }
            // else: cancel → stay on Landing
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Login error: {ex.Message}";
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
        await ShowLoginThenPortalAsync();
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
            HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.7
        };

        var version = new TextBlock
        {
            Text = $"Version {GetAppVersion()}",
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.5
        };

        var copyright = new TextBlock
        {
            Text = "\u00a9 2026 Serhii Zautkin",
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.4
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
