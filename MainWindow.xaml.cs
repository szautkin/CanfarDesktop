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
        appWindow.SetIcon("Assets/canfar.ico");
        appWindow.Title = "CANFAR Science Portal";

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
