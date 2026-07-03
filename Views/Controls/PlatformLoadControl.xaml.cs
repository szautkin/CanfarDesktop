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
        // MetricBar Label/Unit are custom properties, so they are localized here
        // rather than via x:Uid (the XAML literals remain the English fallback).
        CpuBar.Label = Helpers.Loc.T("Load_CpuCores");
        CpuBar.Unit = Helpers.Loc.T("Load_UnitCores");
        RamBar.Label = Helpers.Loc.T("Load_Ram");
        RamBar.Unit = Helpers.Loc.T("Load_UnitGb");
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
                InstancesText.Text = Helpers.Loc.F("Load_InstancesLine",
                    ViewModel.InstancesTotal, ViewModel.InstancesSessions,
                    ViewModel.InstancesDesktopApp, ViewModel.InstancesHeadless);
            }

            LastUpdateText.Text = string.IsNullOrEmpty(ViewModel.LastUpdate)
                ? "" : Helpers.Loc.F("Load_LastUpdated", ViewModel.LastUpdate);

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
