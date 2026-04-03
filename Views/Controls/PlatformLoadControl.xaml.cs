using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.ViewModels;

namespace CanfarDesktop.Views.Controls;

public sealed partial class PlatformLoadControl : UserControl
{
    public PlatformLoadViewModel ViewModel { get; }

    public PlatformLoadControl(PlatformLoadViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ViewModel.PropertyChanged += (_, _) => UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            LoadingBar.Visibility = ViewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;

            CpuBar.Value = ViewModel.CpuUsed;
            CpuBar.MaxValue = ViewModel.CpuAvailable;
            CpuBar.Percent = ViewModel.CpuPercent;

            RamBar.Value = ViewModel.RamUsedGB;
            RamBar.MaxValue = ViewModel.RamAvailableGB;
            RamBar.Percent = ViewModel.RamPercent;

            if (ViewModel.HasInstances)
            {
                InstancesPanel.Visibility = Visibility.Visible;
                InstancesText.Text = $"{ViewModel.InstancesTotal} total  ·  " +
                    $"{ViewModel.InstancesSessions} sessions  ·  " +
                    $"{ViewModel.InstancesDesktopApp} desktop  ·  " +
                    $"{ViewModel.InstancesHeadless} headless";
            }

            LastUpdateText.Text = string.IsNullOrEmpty(ViewModel.LastUpdate)
                ? "" : $"Last updated: {ViewModel.LastUpdate}";

            ErrorBar.IsOpen = ViewModel.HasError;
            ErrorBar.Message = ViewModel.ErrorMessage;
        });
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadStatsCommand.ExecuteAsync(null);
    }

    public async Task LoadAsync()
    {
        await ViewModel.LoadStatsCommand.ExecuteAsync(null);
    }
}
