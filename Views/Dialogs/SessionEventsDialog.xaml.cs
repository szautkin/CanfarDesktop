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
        try
        {
            var eventsTask = vm.GetSessionEventsAsync(sessionId);
            var logsTask = vm.GetSessionLogsAsync(sessionId);

            await Task.WhenAll(eventsTask, logsTask);

            EventsText.Text = eventsTask.Result ?? Helpers.Loc.T("Events_NoEvents");
            LogsText.Text = logsTask.Result ?? Helpers.Loc.T("Events_NoLogs");
        }
        catch (Exception ex)
        {
            // Without this the dialog is stuck on the spinner forever.
            EventsText.Text = Helpers.Loc.F("Events_LoadEventsFailed", ex.Message);
            LogsText.Text = Helpers.Loc.F("Events_LoadLogsFailed", ex.Message);
        }
        finally
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            ContentPivot.Visibility = Visibility.Visible;
            ContentPivot.IsEnabled = true;
        }
    }
}
