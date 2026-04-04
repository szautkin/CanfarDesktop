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
    private enum AppMode { Landing, Portal, Search, Research, Storage, Notebook }

    private readonly MainViewModel _viewModel;
    private readonly LandingView _landingView;
    private DashboardPage? _dashboardPage;
    private SearchPage? _searchPage;
    private ResearchPage? _researchPage;
    private StorageBrowserPage? _storagePage;
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
        LandingContainer.Child = _landingView;

        Activated += OnWindowActivated;
    }

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
        NotebookTabView.Visibility = mode == AppMode.Notebook ? Visibility.Visible : Visibility.Collapsed;
    }

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

    public async void OpenNotebook(string? filePath = null)
    {
        // First time: check for recovery
        if (NotebookTabView.TabItems.Count == 0)
        {
            var page = CreateNotebookTab();
            if (page is not null)
                await page.CheckRecoveryAsync();
        }
        else if (filePath is null)
        {
            CreateNotebookTab();
        }

        // Open file in the current tab if specified
        if (filePath is not null)
        {
            var tab = CreateNotebookTab();
            if (tab is not null)
                await tab.OpenFileAsync(filePath);
        }

        NavigateTo(AppMode.Notebook);
    }

    private NotebookPage? CreateNotebookTab()
    {
        var vm = App.Services.GetRequiredService<ViewModels.Notebook.NotebookViewModel>();
        var page = new NotebookPage(vm);

        var tab = new TabViewItem
        {
            Header = vm.Title,
            Content = page,
            IconSource = new Microsoft.UI.Xaml.Controls.SymbolIconSource { Symbol = Symbol.Document },
        };

        vm.PropertyChanged += (_, e) => DispatcherQueue.TryEnqueue(() =>
        {
            if (e.PropertyName is nameof(vm.Title) or nameof(vm.IsDirty))
                tab.Header = vm.IsDirty ? $"{vm.Title} *" : vm.Title;
        });

        // Wire Ctrl+N from inside a tab to create a new tab
        page.NewTabRequested += () => OpenNotebook();

        NotebookTabView.TabItems.Add(tab);
        NotebookTabView.SelectedItem = tab;
        return page;
    }

    private void OnAddNotebookTab(TabView sender, object args)
    {
        CreateNotebookTab();
    }

    private async void OnNotebookTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Tab.Content is NotebookPage page)
        {
            if (page.ViewModel.IsDirty)
            {
                var dialog = new ContentDialog
                {
                    Title = "Unsaved changes",
                    Content = $"Save changes to {page.ViewModel.Title}?",
                    PrimaryButtonText = "Save",
                    SecondaryButtonText = "Don't Save",
                    CloseButtonText = "Cancel",
                    XamlRoot = Content.XamlRoot,
                };
                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                    await page.ViewModel.SaveCommand.ExecuteAsync(null);
                else if (result == ContentDialogResult.None)
                    return; // Cancel — don't close
            }

            page.ViewModel.Close();
        }

        sender.TabItems.Remove(args.Tab);

        if (sender.TabItems.Count == 0)
            NavigateTo(AppMode.Landing);
    }

    private void OnNotebookTabChanged(object sender, SelectionChangedEventArgs e)
    {
        // Could update title bar or global state here if needed
    }

    private void EnsureResearchPage()
    {
        if (_researchPage is null)
        {
            _researchPage = App.Services.GetRequiredService<ResearchPage>();
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
