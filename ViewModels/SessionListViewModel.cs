using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CanfarDesktop.Models;
using CanfarDesktop.Services;

namespace CanfarDesktop.ViewModels;

public partial class SessionListViewModel : ObservableObject
{
    private readonly ISessionService _sessionService;
    private CancellationTokenSource? _pollCts;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private Session? _selectedSession;

    [ObservableProperty]
    private bool _isPolling;

    [ObservableProperty]
    private int _pollCountdown;

    public ObservableCollection<Session> Sessions { get; } = [];

    public event EventHandler? SessionsRefreshed;

    public SessionListViewModel(ISessionService sessionService)
    {
        _sessionService = sessionService;
    }

    [RelayCommand]
    public async Task LoadSessionsAsync()
    {
        IsLoading = true;
        HasError = false;
        ErrorMessage = string.Empty;

        try
        {
            var sessions = await _sessionService.GetSessionsAsync();
            Sessions.Clear();
            foreach (var session in sessions)
                Sessions.Add(session);

            SessionsRefreshed?.Invoke(this, EventArgs.Empty);

            // Auto-start polling if any session is pending
            if (HasPendingSessions())
                StartPolling();
            else
                StopPolling();
        }
        catch (HttpRequestException ex)
        {
            ErrorMessage = $"Failed to load sessions: {ex.Message}";
            HasError = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Unexpected error: {ex.Message}";
            HasError = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public bool HasPendingSessions() =>
        Sessions.Any(s => s.Status is "Pending" or "Terminating");

    public void StartPolling()
    {
        if (_pollCts is not null) return; // already polling
        _pollCts = new CancellationTokenSource();
        IsPolling = true;
        _ = PollLoopAsync(_pollCts.Token);
    }

    public void StopPolling()
    {
        if (_pollCts is null) return;
        _pollCts.Cancel();
        _pollCts.Dispose();
        _pollCts = null;
        IsPolling = false;
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                PollCountdown = (int)PollInterval.TotalSeconds;
                while (PollCountdown > 0 && !ct.IsCancellationRequested)
                {
                    await Task.Delay(1000, ct);
                    PollCountdown--;
                }

                if (ct.IsCancellationRequested) break;

                await LoadSessionsAsync();

                // LoadSessionsAsync will call StopPolling if no pending sessions remain
                if (!HasPendingSessions()) break;
            }
        }
        catch (TaskCanceledException) { }
    }

    [RelayCommand]
    private async Task DeleteSessionAsync(string sessionId)
    {
        var success = await _sessionService.DeleteSessionAsync(sessionId);
        if (success)
        {
            var session = Sessions.FirstOrDefault(s => s.Id == sessionId);
            if (session is not null)
                Sessions.Remove(session);

            await Task.Delay(3000);
            await LoadSessionsAsync();
        }
    }

    [RelayCommand]
    private async Task RenewSessionAsync(string sessionId)
    {
        var success = await _sessionService.RenewSessionAsync(sessionId);
        if (success)
            await LoadSessionsAsync();
    }

    public async Task<string?> GetSessionEventsAsync(string sessionId)
    {
        return await _sessionService.GetSessionEventsAsync(sessionId);
    }

    public async Task<string?> GetSessionLogsAsync(string sessionId)
    {
        return await _sessionService.GetSessionLogsAsync(sessionId);
    }

    public void OpenSessionInBrowser(Session session)
    {
        if (!string.IsNullOrEmpty(session.ConnectUrl) && session.Status == "Running")
        {
            var uri = new Uri(session.ConnectUrl);
            _ = Windows.System.Launcher.LaunchUriAsync(uri);
        }
    }
}
