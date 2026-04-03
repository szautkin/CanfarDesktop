using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Services;
using CanfarDesktop.Views.Dialogs;

namespace CanfarDesktop.Views.Controls;

public sealed partial class BatchJobsControl : UserControl
{
    private readonly ISessionService _sessionService;
    private List<Session> _headlessSessions = [];
    private Dictionary<string, string> _previousStates = new();
    private bool _isFirstPoll = true;
    private DispatcherTimer? _pollTimer;
    private int _countdown;
    private const int PollSeconds = 45;

    public BatchJobsControl(ISessionService sessionService)
    {
        _sessionService = sessionService;
        InitializeComponent();
        Unloaded += (_, _) => StopPolling();
    }

    public void StopPolling()
    {
        if (_pollTimer is null) return;
        _pollTimer.Tick -= OnTimerTick;
        _pollTimer.Stop();
        _pollTimer = null;
    }

    public async Task LoadAsync()
    {
        await RefreshAsync();
        StartPolling();
    }

    private void StartPolling()
    {
        if (_pollTimer is not null) return;
        _countdown = PollSeconds;
        UpdateCountdownText();
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _pollTimer.Tick += OnTimerTick;
        _pollTimer.Start();
    }

    private async void OnTimerTick(object? sender, object e)
    {
        try
        {
            _countdown--;
            UpdateCountdownText();

            if (_countdown <= 0)
            {
                await RefreshAsync();
                _countdown = PollSeconds;
                UpdateCountdownText();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Batch jobs timer error: {ex.Message}");
        }
    }

    private void UpdateCountdownText()
    {
        CountdownText.Text = $"{_countdown}s";
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
        _countdown = PollSeconds;
        UpdateCountdownText();
    }

    private async Task RefreshAsync()
    {
        try
        {
            var allSessions = await _sessionService.GetSessionsAsync();
            _headlessSessions = allSessions
                .Where(s => string.Equals(s.SessionType, "headless", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var newStates = _headlessSessions.ToDictionary(s => s.Id, s => s.Status);

            if (!_isFirstPoll)
                DetectTransitions(_previousStates, _headlessSessions);

            _previousStates = newStates;
            _isFirstPoll = false;
            UpdateCounts();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Batch jobs refresh failed: {ex.Message}");
        }
    }

    private static void DetectTransitions(Dictionary<string, string> oldStates, List<Session> jobs)
    {
        foreach (var job in jobs)
        {
            if (!oldStates.TryGetValue(job.Id, out var oldStatus)) continue;
            if (IsTerminal(oldStatus)) continue;

            if (IsCompleted(job.Status))
                Helpers.NotificationService.SendJobCompleted(job.SessionName, job.ContainerImage);
            else if (IsFailed(job.Status))
                Helpers.NotificationService.SendJobFailed(job.SessionName, job.ContainerImage);
        }
    }

    private static bool IsTerminal(string status) => IsCompleted(status) || IsFailed(status);
    private static bool IsCompleted(string status) => status is "Succeeded" or "Completed";
    private static bool IsFailed(string status) => status is "Failed" or "Error";

    private void UpdateCounts()
    {
        var groups = BatchJobsHelper.GroupByState(_headlessSessions);
        DispatcherQueue.TryEnqueue(() =>
        {
            PendingCount.Text = groups.Pending.ToString();
            RunningCount.Text = groups.Running.ToString();
            CompletedCount.Text = groups.Completed.ToString();
            FailedCount.Text = groups.Failed.ToString();
        });
    }

    private void OnStateClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string state)
            ShowDialog(state);
    }

    private async void ShowDialog(string initialTab)
    {
        var dialog = new BatchJobsDialog(_headlessSessions, initialTab, XamlRoot.Size, _sessionService)
        {
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }
}
