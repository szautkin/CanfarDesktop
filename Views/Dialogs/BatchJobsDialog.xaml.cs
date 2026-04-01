using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.Models;
using Windows.Foundation;

namespace CanfarDesktop.Views.Dialogs;

public sealed partial class BatchJobsDialog : ContentDialog
{
    private record JobRow(string Id, string SessionName, string ImageLabel);

    private static readonly Dictionary<string, int> StateToTabIndex = new()
    {
        ["Pending"] = 0,
        ["Running"] = 1,
        ["Succeeded"] = 2,
        ["Completed"] = 2,
        ["Failed"] = 3,
        ["Error"] = 3
    };

    public BatchJobsDialog(IReadOnlyList<Session> headlessSessions, string initialTab, Size viewportSize)
    {
        InitializeComponent();

        // Lock dialog to 60% of viewport
        var dialogWidth = viewportSize.Width * 0.6;
        var dialogHeight = viewportSize.Height * 0.6;

        Resources["ContentDialogMinWidth"] = dialogWidth;
        Resources["ContentDialogMaxWidth"] = dialogWidth;

        // Reserve ~120px for dialog title bar + close button area
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

    private static string ParseImageLabel(string imageId)
    {
        var lastSlash = imageId.LastIndexOf('/');
        return lastSlash >= 0 ? imageId[(lastSlash + 1)..] : imageId;
    }
}
