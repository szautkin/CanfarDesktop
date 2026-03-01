using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.ViewModels;

namespace CanfarDesktop.Views.Controls;

public sealed partial class LaunchFormControl : UserControl
{
    public SessionLaunchViewModel ViewModel { get; }

    public event EventHandler? SessionLaunched;

    public LaunchFormControl(SessionLaunchViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ViewModel.IsLaunching))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    LaunchButton.IsEnabled = !ViewModel.IsLaunching;
                    AdvancedLaunchButton.IsEnabled = !ViewModel.IsLaunching;
                });
            }
            else if (e.PropertyName == nameof(ViewModel.IsLoading))
            {
                DispatcherQueue.TryEnqueue(() =>
                    LoadingBar.Visibility = ViewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed);
            }
        };
    }

    private void OnGenerateNameClick(object sender, RoutedEventArgs e)
    {
        ViewModel.GenerateSessionName();
    }

    private void OnResourceTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is RadioButtons rb && rb.SelectedItem is RadioButton selected)
        {
            var tag = selected.Tag?.ToString() ?? "flexible";
            ViewModel.ResourceType = tag;
            ResourcePanel.Visibility = tag == "fixed" ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OnAdvancedResourceTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is RadioButtons rb && rb.SelectedItem is RadioButton selected)
        {
            var tag = selected.Tag?.ToString() ?? "flexible";
            ViewModel.ResourceType = tag;
            AdvancedResourcePanel.Visibility = tag == "fixed" ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private async void OnLaunchClick(object sender, RoutedEventArgs e)
    {
        if (CoresCombo.SelectedItem is int cores) ViewModel.Cores = cores;
        if (RamCombo.SelectedItem is int ram) ViewModel.Ram = ram;
        if (GpuCombo.SelectedItem is int gpus) ViewModel.Gpus = gpus;

        ViewModel.UseCustomImage = false;

        await ViewModel.LaunchCommand.ExecuteAsync(null);

        if (ViewModel.LaunchSuccess)
            SessionLaunched?.Invoke(this, EventArgs.Empty);
    }

    private async void OnAdvancedLaunchClick(object sender, RoutedEventArgs e)
    {
        if (AdvCoresCombo.SelectedItem is int cores) ViewModel.Cores = cores;
        if (AdvRamCombo.SelectedItem is int ram) ViewModel.Ram = ram;
        if (AdvGpuCombo.SelectedItem is int gpus) ViewModel.Gpus = gpus;

        // Sync registry host from combo
        if (RegistryHostCombo.SelectedItem is string host)
            ViewModel.RepositoryHost = host;

        // Sync repo secret (PasswordBox not bindable)
        ViewModel.RepositorySecret = RepoSecretBox.Password;
        ViewModel.UseCustomImage = true;

        await ViewModel.LaunchCommand.ExecuteAsync(null);

        if (ViewModel.LaunchSuccess)
            SessionLaunched?.Invoke(this, EventArgs.Empty);
    }

    public async Task LoadAsync()
    {
        await ViewModel.LoadImagesAndContextAsync();
    }
}
