using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.Models;
using CanfarDesktop.ViewModels;
using CanfarDesktop.Views;
using CanfarDesktop.Views.Dialogs;

namespace CanfarDesktop;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private DashboardPage? _dashboardPage;
    private bool _loginSucceeded;

    public MainWindow()
    {
        InitializeComponent();

        // Set window icon
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
        appWindow.SetIcon("Assets/Verbinal.ico");
        appWindow.Title = "Verbinal - a CANFAR Science Portal";

        _viewModel = App.Services.GetRequiredService<MainViewModel>();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

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
        try
        {
            var loginVm = App.Services.GetRequiredService<LoginViewModel>();
            _loginSucceeded = false;
            loginVm.LoginSucceeded += OnLoginSucceeded;

            var dialog = new LoginDialog(loginVm) { XamlRoot = Content.XamlRoot };
            await dialog.ShowAsync();

            loginVm.LoginSucceeded -= OnLoginSucceeded;

            // Navigate after dialog is fully closed
            if (_loginSucceeded)
                await NavigateToDashboard();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Login error: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"OnLoginClick error: {ex}");
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
            ContentFrame.Content = null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Logout error: {ex}");
        }
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
            _dashboardPage = App.Services.GetRequiredService<DashboardPage>();
            ContentFrame.Content = _dashboardPage;
            await _dashboardPage.LoadDataAsync(_viewModel.Username);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Dashboard error: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"NavigateToDashboard error: {ex}");
        }
    }
}
