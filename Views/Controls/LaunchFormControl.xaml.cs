using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.ViewModels;

namespace CanfarDesktop.Views.Controls;

public sealed partial class LaunchFormControl : UserControl
{
    public SessionLaunchViewModel ViewModel { get; }

    public event EventHandler? LaunchRequested;

    public LaunchFormControl(SessionLaunchViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ViewModel.IsLaunching) or nameof(ViewModel.IsAtSessionLimit))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    var canLaunch = !ViewModel.IsLaunching && !ViewModel.IsAtSessionLimit;
                    LaunchButton.IsEnabled = canLaunch;
                    AdvancedLaunchButton.IsEnabled = canLaunch;
                });
            }
            else if (e.PropertyName == nameof(ViewModel.IsLoading))
            {
                DispatcherQueue.TryEnqueue(() =>
                    LoadingBar.Visibility = ViewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed);
            }

            if (e.PropertyName is nameof(ViewModel.IsAtSessionLimit) or nameof(ViewModel.SessionLimitMessage))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    SessionLimitBar.IsOpen = ViewModel.IsAtSessionLimit;
                    SessionLimitBar.Visibility = ViewModel.IsAtSessionLimit ? Visibility.Visible : Visibility.Collapsed;
                    SessionLimitBar.Message = ViewModel.SessionLimitMessage;
                });
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
            StdResourcePanel.Visibility = tag == "fixed" ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OnAdvancedResourceTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is RadioButtons rb && rb.SelectedItem is RadioButton selected)
        {
            var tag = selected.Tag?.ToString() ?? "flexible";
            ViewModel.ResourceType = tag;
            AdvResourcePanel.Visibility = tag == "fixed" ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OnLaunchClick(object sender, RoutedEventArgs e)
    {
        ViewModel.Cores = StdResourcePanel.Cores;
        ViewModel.Ram = StdResourcePanel.Ram;
        ViewModel.Gpus = StdResourcePanel.Gpus;
        ViewModel.UseCustomImage = false;

        LaunchRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnAdvancedLaunchClick(object sender, RoutedEventArgs e)
    {
        ViewModel.Cores = AdvResourcePanel.Cores;
        ViewModel.Ram = AdvResourcePanel.Ram;
        ViewModel.Gpus = AdvResourcePanel.Gpus;

        if (RegistryHostCombo.SelectedItem is string host)
            ViewModel.RepositoryHost = host;

        ViewModel.RepositorySecret = RepoSecretBox.Password;
        ViewModel.UseCustomImage = true;

        LaunchRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        // Toggle the TeachingTip associated with this help button
        TeachingTip? tip = btn.Name switch
        {
            nameof(StdTypeHelpBtn) => StdTypeTip,
            nameof(StdProjectHelpBtn) => StdProjectTip,
            nameof(StdImageHelpBtn) => StdImageTip,
            nameof(StdNameHelpBtn) => StdNameTip,
            nameof(StdResTypeHelpBtn) => StdResTypeTip,
            nameof(AdvTypeHelpBtn) => AdvTypeTip,
            nameof(AdvImageHelpBtn) => AdvImageTip,
            nameof(AdvAuthHelpBtn) => AdvAuthTip,
            nameof(AdvNameHelpBtn) => AdvNameTip,
            nameof(AdvResTypeHelpBtn) => AdvResTypeTip,
            _ => null
        };

        if (tip is not null)
            tip.IsOpen = !tip.IsOpen;
    }

    public async Task LoadAsync()
    {
        await ViewModel.LoadImagesAndContextAsync();
        ConfigureResourcePanels();
    }

    private void ConfigureResourcePanels()
    {
        var coreOpts = ViewModel.CoreOptions.ToArray();
        var ramOpts = ViewModel.RamOptions.ToArray();
        var gpuOpts = ViewModel.GpuOptions.ToArray();

        StdResourcePanel.Configure(coreOpts, ViewModel.Cores, ramOpts, ViewModel.Ram, gpuOpts);
        AdvResourcePanel.Configure(coreOpts, ViewModel.Cores, ramOpts, ViewModel.Ram, gpuOpts);
    }
}
