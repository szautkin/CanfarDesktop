using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using CanfarDesktop.Models;
using CanfarDesktop.Services;
using Windows.Foundation;

namespace CanfarDesktop.Views.Dialogs;

public sealed partial class BatchJobsDialog : ContentDialog
{
    private record JobRow(string Id, string SessionName, string ImageLabel);

    /// <summary>
    /// A remembered job. Flattened for binding, and built once — a history row shows what was captured,
    /// not what is happening, so nothing in it changes while the dialog is open.
    /// </summary>
    private record HistoryRow(string Name, string Detail, string When, string Glyph, Brush Colour);

    private readonly ISessionService _sessionService;
    private readonly IJobHistoryStore _history;

    private static readonly Dictionary<string, int> StateToTabIndex = new()
    {
        ["Pending"] = 0,
        ["Running"] = 1,
        ["Succeeded"] = 2,
        ["Completed"] = 2,
        ["Failed"] = 4,
        ["Error"] = 4,
        ["History"] = 3
    };

    public BatchJobsDialog(
        IReadOnlyList<Session> headlessSessions,
        string initialTab,
        Size viewportSize,
        ISessionService sessionService,
        IJobHistoryStore? history = null)
    {
        InitializeComponent();
        _sessionService = sessionService;
        _history = history ?? new JobHistoryStore();

        var dialogWidth = viewportSize.Width * 0.6;
        var dialogHeight = viewportSize.Height * 0.6;

        Resources["ContentDialogMinWidth"] = dialogWidth;
        Resources["ContentDialogMaxWidth"] = dialogWidth;

        var contentHeight = dialogHeight - 120;
        ContentRoot.MinHeight = contentHeight;
        ContentRoot.MaxHeight = contentHeight;

        var pending = new List<JobRow>();
        var running = new List<JobRow>();
        var completed = new List<JobRow>();
        var failed = new List<JobRow>();

        foreach (var s in headlessSessions)
        {
            var row = new JobRow(s.Id, s.SessionName, ParseImageLabel(s.ContainerImage));
            switch (s.Status)
            {
                case "Pending": pending.Add(row); break;
                case "Running": running.Add(row); break;
                case "Succeeded" or "Completed": completed.Add(row); break;
                case "Failed" or "Error": failed.Add(row); break;
            }
        }

        PendingHeader.Text = Helpers.Loc.F("Batch_PendingTab", pending.Count);
        RunningHeader.Text = Helpers.Loc.F("Batch_RunningTab", running.Count);
        CompletedHeader.Text = Helpers.Loc.F("Batch_CompletedTab", completed.Count);
        FailedHeader.Text = Helpers.Loc.F("Batch_FailedTab", failed.Count);

        PendingList.ItemsSource = pending;
        RunningList.ItemsSource = running;
        CompletedList.ItemsSource = completed;
        FailedList.ItemsSource = failed;

        PopulateHistory();

        if (StateToTabIndex.TryGetValue(initialTab, out var idx))
            TabPivot.SelectedIndex = idx;
    }

    /// <summary>
    /// What the app remembers about jobs CANFAR no longer has.
    ///
    /// The four live tabs above are a view of the sessions listing, which forgets a job shortly after it
    /// ends. This tab is the only place a failure from an hour ago still has a reason attached.
    /// </summary>
    private void PopulateHistory()
    {
        var jobs = _history.All();

        HistoryHeader.Text = Helpers.Loc.F("Batch_HistoryTab", jobs.Count);
        HistoryList.ItemsSource = jobs.Select(Row).ToList();
        HistoryEmptyText.Visibility = jobs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearHistoryButton.Visibility = jobs.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private static HistoryRow Row(JobRecord job)
    {
        var failed = job.Outcome == JobOutcome.Failed;

        // The reason, when there is one. Otherwise what it was — a probe the app ran, or a job the user
        // submitted — because "Succeeded" on its own says nothing a green tick has not already said.
        var detail = failed && job.FailureReason is { Length: > 0 } why
            ? $"{job.Summary} — {why}"
            : job.Summary;

        return new HistoryRow(
            job.Name,
            detail,
            FormatWhen(job.FinishedAt),
            failed ? "" : "",
            Brush(failed ? "SystemFillColorCriticalBrush" : "SystemFillColorSuccessBrush"));
    }

    /// <summary>
    /// The stored instant in the reader's own time zone, or the raw string when it will not parse.
    ///
    /// Showing the raw value beats showing nothing: a timestamp this app failed to read is still a
    /// timestamp somebody can compare against a log.
    /// </summary>
    private static string FormatWhen(string iso)
        => DateTimeOffset.TryParse(iso, out var at) ? at.ToLocalTime().ToString("g") : iso;

    private static Brush Brush(string key)
        => Application.Current.Resources[key] as Brush
            ?? (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

    private void OnClearHistory(object sender, RoutedEventArgs e)
    {
        _history.Clear();
        PopulateHistory();
    }

    private void OnEventsClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string sessionId })
            ShowEventsDialog(sessionId);
    }

    private void OnLogsClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string sessionId })
            ShowLogsDialog(sessionId);
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string sessionId }) return;

        Hide(); // Must close before showing another ContentDialog
        var confirm = new ContentDialog
        {
            Title = Helpers.Loc.T("Batch_DeleteJobTitle"),
            Content = Helpers.Loc.F("Batch_DeleteJobConfirm", sessionId),
            PrimaryButtonText = Helpers.Loc.T("Portal_Delete"),
            CloseButtonText = Helpers.Loc.T("Portal_Cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await confirm.ShowAsync() == ContentDialogResult.Primary)
        {
            await _sessionService.DeleteSessionAsync(sessionId);
        }
        Reopen();
    }

    // Child dialogs require hiding this one (single-ContentDialog rule); bring
    // the user back to the Batch Jobs list afterwards instead of stranding them
    // at the main window.
    private void Reopen()
    {
        try { _ = ShowAsync(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Batch jobs reopen failed: {ex.Message}"); }
    }

    private async void ShowEventsDialog(string sessionId)
    {
        Hide();
        var events = await _sessionService.GetSessionEventsAsync(sessionId);
        var dialog = new ContentDialog
        {
            Title = Helpers.Loc.F("Batch_EventsTitle", sessionId),
            Content = new ScrollViewer
            {
                MaxHeight = 400,
                Content = new TextBlock
                {
                    Text = events ?? Helpers.Loc.T("Events_NoEvents"),
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                    FontSize = 12
                }
            },
            CloseButtonText = Helpers.Loc.T("Common_Close"),
            XamlRoot = XamlRoot
        };
        dialog.Resources["ContentDialogMinWidth"] = 600.0;
        await dialog.ShowAsync();
        Reopen();
    }

    private async void ShowLogsDialog(string sessionId)
    {
        Hide();
        var logs = await _sessionService.GetSessionLogsAsync(sessionId);
        var dialog = new ContentDialog
        {
            Title = Helpers.Loc.F("Batch_LogsTitle", sessionId),
            Content = new ScrollViewer
            {
                MaxHeight = 400,
                Content = new TextBlock
                {
                    Text = logs ?? Helpers.Loc.T("Events_NoLogs"),
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                    FontSize = 12
                }
            },
            CloseButtonText = Helpers.Loc.T("Common_Close"),
            XamlRoot = XamlRoot
        };
        dialog.Resources["ContentDialogMinWidth"] = 600.0;
        await dialog.ShowAsync();
        Reopen();
    }

    private static string ParseImageLabel(string imageId)
    {
        var lastSlash = imageId.LastIndexOf('/');
        return lastSlash >= 0 ? imageId[(lastSlash + 1)..] : imageId;
    }
}
