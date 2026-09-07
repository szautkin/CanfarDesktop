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
    private readonly IJobHistoryStore _history;
    private List<Session> _headlessSessions = [];
    private Dictionary<string, string> _previousStates = new();
    private bool _isFirstPoll = true;
    private DispatcherTimer? _pollTimer;
    private int _countdown;

    /// <summary>
    /// How long before asking again — adaptive, because the interval IS the notification delay.
    ///
    /// The card used to run at a flat 45 seconds, so a job that started and finished inside one window
    /// was never seen in a non-terminal state and no completion was ever announced. See
    /// <see cref="PollCadence"/>.
    /// </summary>
    private PollCadence _cadence = new(PollCadence.JobsWatchSeconds);

    public BatchJobsControl(ISessionService sessionService, IJobHistoryStore? history = null)
    {
        _sessionService = sessionService;
        _history = history ?? new JobHistoryStore();
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
        _countdown = _cadence.Seconds;
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
                _countdown = _cadence.Seconds;
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
        CountdownText.Text = Helpers.Loc.F("Portal_CountdownSeconds", _countdown);
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
        _countdown = _cadence.Seconds;
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

            // What the next interval is decided from. In flight: any job that can still change state,
            // so a card of finished jobs costs nothing to watch. Changed: any job that moved, appeared
            // or went away since last time.
            var inFlight = _headlessSessions.Any(s => !IsTerminal(s.Status));
            var changed = newStates.Count != _previousStates.Count
                || newStates.Any(kv => !_previousStates.TryGetValue(kv.Key, out var was) || was != kv.Value);

            _previousStates = newStates;
            _isFirstPoll = false;
            _cadence.Observe(inFlight, changed);
            UpdateCounts();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Batch jobs refresh failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Announce the jobs that just finished, and write them down.
    ///
    /// The notification is a moment; the record is what survives. Skaha reaps headless jobs, so a job
    /// can fail here and be gone from the listing a minute later — leaving a dismissed toast and a count
    /// that ticked from Running to Failed as the only trace that anything happened.
    /// </summary>
    private void DetectTransitions(Dictionary<string, string> oldStates, List<Session> jobs)
    {
        foreach (var job in jobs)
        {
            if (!oldStates.TryGetValue(job.Id, out var oldStatus)) continue;
            if (IsTerminal(oldStatus)) continue;

            if (IsCompleted(job.Status))
            {
                Helpers.NotificationService.SendJobCompleted(job.SessionName, job.ContainerImage);
                Remember(job, JobOutcome.Succeeded);
            }
            else if (IsFailed(job.Status))
            {
                Helpers.NotificationService.SendJobFailed(job.SessionName, job.ContainerImage);
                Remember(job, JobOutcome.Failed);
            }
        }
    }

    private void Remember(Session job, JobOutcome outcome)
    {
        try
        {
            _history.Record(new JobRecord
            {
                Id = job.Id,
                Name = job.SessionName,
                Image = job.ContainerImage,
                Origin = JobOrigin.User,
                Outcome = outcome,
                Status = job.Status,
                StartedAt = job.StartedTime,
                FinishedAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),

                // Skaha's status is all there is at this point. The reason, when there is one to get, is
                // fetched by whoever opens the job — while it still exists.
                FailureReason = outcome == JobOutcome.Failed ? job.Status : null,
            });
        }
        catch (Exception ex)
        {
            // Remembering is a convenience. Failing to remember must not break the card.
            System.Diagnostics.Debug.WriteLine($"Job history write failed: {ex.Message}");
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
        var dialog = new BatchJobsDialog(_headlessSessions, initialTab, XamlRoot.Size, _sessionService, _history)
        {
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }
}
