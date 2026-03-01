using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CanfarDesktop.Models;
using CanfarDesktop.Services;

namespace CanfarDesktop.ViewModels;

public partial class SessionListViewModel : ObservableObject
{
    private readonly ISessionService _sessionService;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private Session? _selectedSession;

    public ObservableCollection<Session> Sessions { get; } = [];

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
