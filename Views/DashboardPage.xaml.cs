using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.Models;
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
    private readonly SessionListViewModel _sessionListVm;
    private readonly SessionLaunchViewModel _sessionLaunchVm;

    public DashboardPage(
        SessionListViewModel sessionListVm,
        SessionLaunchViewModel sessionLaunchVm,
        PlatformLoadViewModel platformLoadVm,
        StorageViewModel storageVm)
    {
        InitializeComponent();

        _sessionListVm = sessionListVm;
        _sessionLaunchVm = sessionLaunchVm;
        _sessionList = new SessionListControl(sessionListVm);
        _launchForm = new LaunchFormControl(sessionLaunchVm);
        _platformLoad = new PlatformLoadControl(platformLoadVm);
        _storageQuota = new StorageQuotaControl(storageVm);

        SessionListContainer.Child = _sessionList;
        LaunchFormContainer.Child = _launchForm;
        PlatformLoadContainer.Child = _platformLoad;
        StorageContainer.Child = _storageQuota;

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
        _launchForm.SessionLaunched += OnSessionLaunched;

        // Update session limit whenever sessions are refreshed (including by polling)
        _sessionListVm.SessionsRefreshed += (_, _) => _sessionLaunchVm.UpdateSessionLimit();
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
        await _sessionListVm.RenewSessionCommand.ExecuteAsync(sessionId);
    }

    private async void OnSessionEvents(object? sender, string sessionId)
    {
        var events = await _sessionListVm.GetSessionEventsAsync(sessionId);
        if (events is not null)
        {
            var dialog = new SessionEventsDialog { XamlRoot = XamlRoot };
            dialog.SetContent(events);
            await dialog.ShowAsync();
        }
    }

    private async void OnSessionLaunched(object? sender, EventArgs e)
    {
        await Task.Delay(1000);
        await _sessionList.LoadAsync();
    }
}
