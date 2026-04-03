using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.Models;
using CanfarDesktop.Services.HttpClients;
using CanfarDesktop.ViewModels;
using CanfarDesktop.Views;
using CanfarDesktop.Views.Dialogs;
using static CanfarDesktop.Views.WindowHelper;

namespace CanfarDesktop;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private DashboardPage? _dashboardPage;
    private SearchPage? _searchPage;
    private bool _loginSucceeded;
    private string _currentNav = "portal";

    public MainWindow()
    {
        InitializeComponent();
        TrackWindow(this);

        // Set window icon
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
        appWindow.SetIcon("Assets/Verbinal.ico");
        appWindow.Title = "Verbinal - a CANFAR Science Portal";

        _viewModel = App.Services.GetRequiredService<MainViewModel>();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.TokenExpired += OnTokenExpired;

        // Subscribe to 401 detection from any HttpClient
        var tokenProvider = App.Services.GetRequiredService<AuthTokenProvider>();
        tokenProvider.Unauthorized += OnUnauthorized;

        Activated += OnWindowActivated;
    }

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

            if (_viewModel.IsAuthenticated)
                await NavigateToDashboard();
        }
        catch (Exception ex)
        {
            AuthProgress.IsActive = false;
            StatusText.Text = $"Startup error: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"InitializeAsync error: {ex}");
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (e.PropertyName is "IsAuthenticated" or "Username" or "StatusMessage")
                UpdateAuthUI();
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

    private async void OnAboutClick(object sender, RoutedEventArgs e)
    {
        var logo = new Image
        {
            Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/Verbinal_icon.png")),
            Width = 64,
            Height = 64,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var title = new TextBlock
        {
            Text = "Verbinal",
            Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["SubtitleTextBlockStyle"],
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var subtitle = new TextBlock
        {
            Text = "A CANFAR Science Portal Companion",
            Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["BodyTextBlockStyle"],
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity = 0.7
        };

        var version = new TextBlock
        {
            Text = $"Version {GetAppVersion()}",
            Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["CaptionTextBlockStyle"],
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity = 0.5
        };

        var description = new TextBlock
        {
            Text = "Verbinal is a desktop companion for the CANFAR (Canadian Advanced Network " +
                   "for Astronomical Research) web portal at canfar.net.\n\n" +
                   "Launch, monitor, and manage your interactive computing sessions " +
                   "(Notebook, Desktop, CARTA, Firefly) directly from your desktop " +
                   "without needing a browser.\n\n" +
                   "CANFAR is operated by the Canadian Astronomy Data Centre (CADC) " +
                   "and the Digital Research Alliance of Canada.",
            TextWrapping = TextWrapping.Wrap,
            Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Opacity = 0.6
        };

        var license = new TextBlock
        {
            Text = "Licensed under the GNU Affero General Public License v3.0 (AGPL-3.0).\n" +
                   "This is free software: you can redistribute it and/or modify it under " +
                   "the terms of the AGPL. See the LICENSE file for details.",
            TextWrapping = TextWrapping.Wrap,
            Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Opacity = 0.5
        };

        var separator = new Border
        {
            Height = 1,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.Gray),
            Opacity = 0.3,
            Margin = new Thickness(0, 4, 0, 4)
        };

        var appInfo = new TextBlock
        {
            Text = $"Runtime: .NET {Environment.Version}\n" +
                   $"Platform: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}\n" +
                   $"Architecture: {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}\n" +
                   $"Framework: Windows App SDK / WinUI 3",
            TextWrapping = TextWrapping.Wrap,
            Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Opacity = 0.4
        };

        var copyright = new TextBlock
        {
            Text = "\u00a9 2026 Serhii Zautkin",
            Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["CaptionTextBlockStyle"],
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity = 0.4
        };

        var link = new HyperlinkButton
        {
            Content = "Visit canfar.net",
            NavigateUri = new Uri("https://www.canfar.net"),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var panel = new StackPanel { Spacing = 12, MinWidth = 350 };
        panel.Children.Add(logo);
        panel.Children.Add(title);
        panel.Children.Add(subtitle);
        panel.Children.Add(version);
        panel.Children.Add(description);
        panel.Children.Add(license);
        panel.Children.Add(separator);
        panel.Children.Add(appInfo);
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

    private async void OnLoginClick(object sender, RoutedEventArgs e)
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
                await NavigateToDashboard();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Login error: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"OnLoginClick error: {ex}");
        }
        finally
        {
            if (loginVm is not null)
                loginVm.LoginSucceeded -= OnLoginSucceeded;
        }
    }

    private void OnLoginSucceeded(string username, UserInfo? userInfo)
    {
        _viewModel.UpdateAuthState(username, userInfo);
        _loginSucceeded = true;
    }

    private async void OnLogoutClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.LogoutCommand.ExecuteAsync(null);
            _dashboardPage = null;
            PortalContainer.Child = null;
            PortalContainer.Visibility = Visibility.Visible;
            SearchContainer.Visibility = Visibility.Collapsed;
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

        try
        {
            await _viewModel.HandleTokenExpiredAsync();
        }
        finally
        {
            _isHandlingUnauthorized = false;
        }
    }

    private async void OnTokenExpired(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            _dashboardPage = null;
            PortalContainer.Child = null;
            PortalContainer.Visibility = Visibility.Visible;
            SearchContainer.Visibility = Visibility.Collapsed;
            await Task.Delay(100);
            OnLoginClick(this, new RoutedEventArgs());
        });
    }

    private static string GetAppVersion()
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        return version is not null
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : "0.0.0";
    }

    private async Task NavigateToDashboard()
    {
        try
        {
            if (_dashboardPage is null)
            {
                _dashboardPage = App.Services.GetRequiredService<DashboardPage>();
                PortalContainer.Child = _dashboardPage;
                await _dashboardPage.LoadDataAsync(_viewModel.Username);
            }

            PortalContainer.Visibility = Visibility.Visible;
            SearchContainer.Visibility = Visibility.Collapsed;
            UpdateNavButtons("portal");
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Dashboard error: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"NavigateToDashboard error: {ex}");
        }
    }

    private async void OnNavigatePortal(object sender, RoutedEventArgs e)
    {
        if (_currentNav == "portal") return;
        if (!_viewModel.IsAuthenticated)
        {
            StatusText.Text = "Please log in to access Portal.";
            return;
        }
        await NavigateToDashboard();
    }

    private async void OnNavigateSearch(object sender, RoutedEventArgs e)
    {
        if (_currentNav == "search") return;
        try
        {
            if (_searchPage is null)
            {
                _searchPage = App.Services.GetRequiredService<SearchPage>();
                SearchContainer.Child = _searchPage;
                _searchPage.LoadAsync();
            }

            SearchContainer.Visibility = Visibility.Visible;
            PortalContainer.Visibility = Visibility.Collapsed;
            UpdateNavButtons("search");
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Search error: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"OnNavigateSearch error: {ex}");
        }
    }

    private void UpdateNavButtons(string active)
    {
        _currentNav = active;
        var accent = (Style)Application.Current.Resources["AccentButtonStyle"];

        // Reset to default by clearing the explicit style
        PortalNavButton.ClearValue(FrameworkElement.StyleProperty);
        SearchNavButton.ClearValue(FrameworkElement.StyleProperty);

        if (active == "portal") PortalNavButton.Style = accent;
        else if (active == "search") SearchNavButton.Style = accent;
    }
}
