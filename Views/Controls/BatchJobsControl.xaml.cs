using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.Helpers;
using CanfarDesktop.ViewModels;
using CanfarDesktop.Views.Dialogs;

namespace CanfarDesktop.Views.Controls;

public sealed partial class BatchJobsControl : UserControl
{
    private readonly SessionListViewModel _vm;

    public BatchJobsControl(SessionListViewModel viewModel)
    {
        _vm = viewModel;
        InitializeComponent();
        _vm.SessionsRefreshed += (_, _) => UpdateCounts();
    }

    private void UpdateCounts()
    {
        var groups = BatchJobsHelper.GroupByState(_vm.HeadlessSessions);
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
        var dialog = new BatchJobsDialog(_vm.HeadlessSessions, initialTab, XamlRoot.Size)
        {
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }
}
