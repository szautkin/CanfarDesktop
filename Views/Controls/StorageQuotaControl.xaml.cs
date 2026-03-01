using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.ViewModels;

namespace CanfarDesktop.Views.Controls;

public sealed partial class StorageQuotaControl : UserControl
{
    public StorageViewModel ViewModel { get; }

    public StorageQuotaControl(StorageViewModel viewModel)
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
            DataPanel.Visibility = ViewModel.HasData ? Visibility.Visible : Visibility.Collapsed;

            UsedText.Text = $"{ViewModel.UsedGB:F2} GB";
            QuotaText.Text = $"{ViewModel.QuotaGB:F2} GB";
            UsageText.Text = $"{ViewModel.UsagePercent:F1}%";
            UsageBar.Value = ViewModel.UsagePercent;

            ErrorBar.IsOpen = ViewModel.HasError;
            ErrorBar.Message = ViewModel.ErrorMessage;
        });
    }

    public async Task LoadAsync(string username)
    {
        await ViewModel.LoadQuotaCommand.ExecuteAsync(username);
    }
}
