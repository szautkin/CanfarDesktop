using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.Models;
using CanfarDesktop.Services;
using CanfarDesktop.ViewModels;
using CanfarDesktop.Views.Controls;
using CanfarDesktop.Views.Dialogs;

namespace CanfarDesktop.Views;

public sealed partial class DashboardPage : Page
{
    private readonly SessionListControl _sessionList;
    private readonly LaunchFormControl _launchForm;
    private readonly PlatformLoadControl _platformLoad;
    private readonly StorageQuotaControl _storageQuota;
    private readonly RecentLaunchesControl _recentLaunches;
    private readonly SessionListViewModel _sessionListVm;
    private readonly SessionLaunchViewModel _sessionLaunchVm;

    public DashboardPage(
        SessionListViewModel sessionListVm,
        SessionLaunchViewModel sessionLaunchVm,
        PlatformLoadViewModel platformLoadVm,
        StorageViewModel storageVm,
        IRecentLaunchService recentLaunchService)
    {
        InitializeComponent();

        _sessionListVm = sessionListVm;
        _sessionLaunchVm = sessionLaunchVm;
        _sessionList = new SessionListControl(sessionListVm);
        _launchForm = new LaunchFormControl(sessionLaunchVm);
        _platformLoad = new PlatformLoadControl(platformLoadVm);
        _storageQuota = new StorageQuotaControl(storageVm);
        _recentLaunches = new RecentLaunchesControl(recentLaunchService);

        SessionListContainer.Child = _sessionList;
        LaunchFormContainer.Child = _launchForm;
        PlatformLoadContainer.Child = _platformLoad;
        StorageContainer.Child = _storageQuota;
        RecentLaunchesContainer.Child = _recentLaunches;

        // Wire up session counter for name generation
        sessionLaunchVm.SetSessionCounter(type =>
            _sessionListVm.Sessions.Count(s => s.SessionType == type));

        // Wire up total session counter for launch limit
        sessionLaunchVm.SetTotalSessionCounter(() => _sessionListVm.Sessions.Count);

        // Wire up events
        _sessionList.SessionOpenRequested += OnSessionOpen;
        _sessionList.SessionDeleteRequested += OnSessionDelete;
        _sessionList.SessionRenewRequested += OnSessionRenew;
        _sessionList.SessionEventsRequested += OnSessionEvents;
        _launchForm.LaunchRequested += OnLaunchRequested;
        _recentLaunches.RelaunchRequested += OnRelaunchRequested;

        // Update session limit whenever sessions are refreshed (including by polling)
        _sessionListVm.SessionsRefreshed += (_, _) =>
        {
            _sessionLaunchVm.UpdateSessionLimit();
            _recentLaunches.UpdateSessionLimit(_sessionLaunchVm.IsAtSessionLimit);
        };
    }

    public async Task LoadDataAsync(string? username)
    {
        var tasks = new List<Task>
        {
            SafeLoadAsync(_sessionList.LoadAsync(), "sessions"),
            SafeLoadAsync(_launchForm.LoadAsync(), "launch form"),
            SafeLoadAsync(_platformLoad.LoadAsync(), "platform stats")
        };

        if (!string.IsNullOrEmpty(username))
            tasks.Add(SafeLoadAsync(_storageQuota.LoadAsync(username), "storage"));

        await Task.WhenAll(tasks);
    }

    private static async Task SafeLoadAsync(Task task, string name)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load {name}: {ex}");
        }
    }

    private void OnSessionOpen(object? sender, string sessionId)
    {
        var session = _sessionListVm.Sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session is not null)
            _sessionListVm.OpenSessionInBrowser(session);
    }

    private async void OnSessionDelete(object? sender, string sessionId)
    {
        var session = _sessionListVm.Sessions.FirstOrDefault(s => s.Id == sessionId);
        var dialog = new DeleteSessionDialog { XamlRoot = XamlRoot };
        dialog.SetSessionName(session?.SessionName ?? sessionId);

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
            await _sessionListVm.DeleteSessionCommand.ExecuteAsync(sessionId);
    }

    private async void OnSessionRenew(object? sender, string sessionId)
    {
        var session = _sessionListVm.Sessions.FirstOrDefault(s => s.Id == sessionId);
        var sessionName = session?.SessionName ?? sessionId;

        await ShowRenewDialogAsync(sessionName, sessionId);
    }

    private async void OnSessionEvents(object? sender, string sessionId)
    {
        var dialog = new SessionEventsDialog { XamlRoot = XamlRoot };
        var loadTask = dialog.LoadAsync(sessionId, _sessionListVm);
        _ = dialog.ShowAsync();
        await loadTask;
    }

    private async void OnLaunchRequested(object? sender, EventArgs e)
    {
        var name = _sessionLaunchVm.SessionName;
        var imageLabel = _sessionLaunchVm.UseCustomImage
            ? _sessionLaunchVm.CustomImageUrl
            : _sessionLaunchVm.SelectedImage?.Label ?? "";
        var resourceType = _sessionLaunchVm.ResourceType;
        var cores = _sessionLaunchVm.Cores;
        var ram = _sessionLaunchVm.Ram;
        var gpus = _sessionLaunchVm.Gpus;

        await ShowLaunchDialogAsync(
            title: "Launch Session",
            name: name,
            imageLabel: imageLabel,
            resourceType: resourceType,
            cores: cores,
            ram: ram,
            gpus: gpus,
            launchFunc: async () =>
            {
                await _sessionLaunchVm.LaunchCommand.ExecuteAsync(null);
                return _sessionLaunchVm.LaunchSuccess;
            });
    }

    private async void OnRelaunchRequested(object? sender, RecentLaunch launch)
    {
        await ShowLaunchDialogAsync(
            title: "Relaunch Session",
            name: launch.Name,
            imageLabel: launch.ImageLabel,
            resourceType: launch.ResourceType,
            cores: launch.Cores,
            ram: launch.Ram,
            gpus: launch.Gpus,
            launchFunc: () => _sessionLaunchVm.RelaunchAsync(launch));
    }

    private async Task ShowLaunchDialogAsync(
        string title, string name, string imageLabel,
        string resourceType, int cores, int ram, int gpus,
        Func<Task<bool>> launchFunc)
    {
        var statusText = new TextBlock
        {
            Text = $"Launching {name}...",
            Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
            VerticalAlignment = VerticalAlignment.Center
        };
        var progressRing = new ProgressRing { IsActive = true, Width = 20, Height = 20 };

        var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        statusRow.Children.Add(progressRing);
        statusRow.Children.Add(statusText);

        var resourceSummary = resourceType == "fixed"
            ? $"CPU: {cores}  \u00B7  RAM: {ram}GB" + (gpus > 0 ? $"  \u00B7  GPU: {gpus}" : "")
            : "Flexible resources";
        var detailText = new TextBlock
        {
            Text = $"{imageLabel}  \u00B7  {resourceSummary}",
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Opacity = 0.6
        };

        var resultBar = new InfoBar
        {
            IsOpen = false,
            IsClosable = false,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(statusRow);
        panel.Children.Add(detailText);
        panel.Children.Add(resultBar);

        var dialog = new ContentDialog
        {
            Title = title,
            Content = panel,
            XamlRoot = XamlRoot,
            CloseButtonText = "Close"
        };

        var launchTask = launchFunc();
        var dialogTask = dialog.ShowAsync();

        var success = await launchTask;

        progressRing.IsActive = false;
        statusRow.Children.Remove(progressRing);

        if (success)
        {
            statusText.Text = $"{name} launched successfully!";
            resultBar.Severity = InfoBarSeverity.Success;
            resultBar.Title = "Session is starting";
            resultBar.Message = "It will appear in Active Sessions shortly.";
            resultBar.IsOpen = true;

            _recentLaunches.Refresh();

            await Task.Delay(2000);
            dialog.Hide();

            await Task.Delay(1000);
            await _sessionList.LoadAsync();
        }
        else
        {
            statusText.Text = "Launch failed";
            resultBar.Severity = InfoBarSeverity.Error;
            resultBar.Title = "Error";
            resultBar.Message = _sessionLaunchVm.ErrorMessage;
            resultBar.IsOpen = true;
        }
    }

    private async Task ShowRenewDialogAsync(string sessionName, string sessionId)
    {
        var statusText = new TextBlock
        {
            Text = $"Renewing '{sessionName}'...",
            Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
            VerticalAlignment = VerticalAlignment.Center
        };
        var progressRing = new ProgressRing { IsActive = true, Width = 20, Height = 20 };

        var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        statusRow.Children.Add(progressRing);
        statusRow.Children.Add(statusText);

        var resultBar = new InfoBar
        {
            IsOpen = false,
            IsClosable = false,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(statusRow);
        panel.Children.Add(resultBar);

        var dialog = new ContentDialog
        {
            Title = "Renew Session",
            Content = panel,
            XamlRoot = XamlRoot,
            CloseButtonText = "Close"
        };

        var renewTask = _sessionListVm.TryRenewSessionAsync(sessionId);
        var dialogTask = dialog.ShowAsync();

        var (success, errorMessage) = await renewTask;

        progressRing.IsActive = false;
        statusRow.Children.Remove(progressRing);

        if (success)
        {
            statusText.Text = $"'{sessionName}' renewed successfully!";
            resultBar.Severity = InfoBarSeverity.Success;
            resultBar.Title = "Session renewed";
            resultBar.Message = "The session expiry time has been extended.";
            resultBar.IsOpen = true;

            await Task.Delay(2000);
            dialog.Hide();
        }
        else
        {
            statusText.Text = "Renew failed";
            resultBar.Severity = InfoBarSeverity.Error;
            resultBar.Title = "Error";
            resultBar.Message = errorMessage ?? "Unknown error";
            resultBar.IsOpen = true;
        }
    }
}
