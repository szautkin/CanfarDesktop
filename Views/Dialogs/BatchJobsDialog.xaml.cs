using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.Models;
using CanfarDesktop.Services;
using Windows.Foundation;

namespace CanfarDesktop.Views.Dialogs;

public sealed partial class BatchJobsDialog : ContentDialog
{
    private record JobRow(string Id, string SessionName, string ImageLabel);

    private readonly ISessionService _sessionService;

    private static readonly Dictionary<string, int> StateToTabIndex = new()
    {
        ["Pending"] = 0,
        ["Running"] = 1,
        ["Succeeded"] = 2,
        ["Completed"] = 2,
        ["Failed"] = 3,
        ["Error"] = 3
    };

    public BatchJobsDialog(
        IReadOnlyList<Session> headlessSessions,
        string initialTab,
        Size viewportSize,
        ISessionService sessionService)
    {
        InitializeComponent();
        _sessionService = sessionService;

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

        PendingHeader.Text = $"Pending ({pending.Count})";
        RunningHeader.Text = $"Running ({running.Count})";
        CompletedHeader.Text = $"Completed ({completed.Count})";
        FailedHeader.Text = $"Failed ({failed.Count})";

        PendingList.ItemsSource = pending;
        RunningList.ItemsSource = running;
        CompletedList.ItemsSource = completed;
        FailedList.ItemsSource = failed;

        if (StateToTabIndex.TryGetValue(initialTab, out var idx))
            TabPivot.SelectedIndex = idx;
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

        var confirm = new ContentDialog
        {
            Title = "Delete Batch Job",
            Content = $"Delete job {sessionId}?",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await confirm.ShowAsync() == ContentDialogResult.Primary)
        {
            await _sessionService.DeleteSessionAsync(sessionId);
            // Close dialog — parent will refresh on next poll
            Hide();
        }
    }

    private async void ShowEventsDialog(string sessionId)
    {
        Hide();
        var events = await _sessionService.GetSessionEventsAsync(sessionId);
        var dialog = new ContentDialog
        {
            Title = $"Events — {sessionId}",
            Content = new ScrollViewer
            {
                MaxHeight = 400,
                Content = new TextBlock
                {
                    Text = events ?? "No events available",
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                    FontSize = 12
                }
            },
            CloseButtonText = "Close",
            XamlRoot = XamlRoot
        };
        dialog.Resources["ContentDialogMinWidth"] = 600.0;
        await dialog.ShowAsync();
    }

    private async void ShowLogsDialog(string sessionId)
    {
        Hide();
        var logs = await _sessionService.GetSessionLogsAsync(sessionId);
        var dialog = new ContentDialog
        {
            Title = $"Logs — {sessionId}",
            Content = new ScrollViewer
            {
                MaxHeight = 400,
                Content = new TextBlock
                {
                    Text = logs ?? "No logs available",
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                    FontSize = 12
                }
            },
            CloseButtonText = "Close",
            XamlRoot = XamlRoot
        };
        dialog.Resources["ContentDialogMinWidth"] = 600.0;
        await dialog.ShowAsync();
    }

    private static string ParseImageLabel(string imageId)
    {
        var lastSlash = imageId.LastIndexOf('/');
        return lastSlash >= 0 ? imageId[(lastSlash + 1)..] : imageId;
    }
}
