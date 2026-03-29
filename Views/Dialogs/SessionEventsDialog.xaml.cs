using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.ViewModels;

namespace CanfarDesktop.Views.Dialogs;

public sealed partial class SessionEventsDialog : ContentDialog
{
    public SessionEventsDialog()
    {
        InitializeComponent();
    }

    public async Task LoadAsync(string sessionId, SessionListViewModel vm)
    {
        var eventsTask = vm.GetSessionEventsAsync(sessionId);
        var logsTask = vm.GetSessionLogsAsync(sessionId);

        await Task.WhenAll(eventsTask, logsTask);

        EventsText.Text = eventsTask.Result ?? "No events available";
        LogsText.Text = logsTask.Result ?? "No logs available";

        LoadingPanel.Visibility = Visibility.Collapsed;
        ContentPivot.Visibility = Visibility.Visible;
        ContentPivot.IsEnabled = true;
    }
}
