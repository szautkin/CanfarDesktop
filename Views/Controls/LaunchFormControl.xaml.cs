using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.ViewModels;

namespace CanfarDesktop.Views.Controls;

public sealed partial class LaunchFormControl : UserControl
{
    public SessionLaunchViewModel ViewModel { get; }

    public event EventHandler? LaunchRequested;
    public event EventHandler? HeadlessLaunchRequested;

    public LaunchFormControl(SessionLaunchViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        HeadlessLaunchLabel.Text = Helpers.Loc.T("Launch_LaunchJob");
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ViewModel.IsLaunching) or nameof(ViewModel.IsAtSessionLimit))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    var canLaunch = !ViewModel.IsLaunching && !ViewModel.IsAtSessionLimit;
                    LaunchButton.IsEnabled = canLaunch;
                    AdvancedLaunchButton.IsEnabled = canLaunch;
                    // Headless jobs aren't bound by the interactive-session cap.
                    HeadlessLaunchButton.IsEnabled = !ViewModel.IsLaunching;
                });
            }
            else if (e.PropertyName is nameof(ViewModel.HasHeadlessImages))
            {
                DispatcherQueue.TryEnqueue(UpdateHeadlessAvailability);
            }
            else if (e.PropertyName is nameof(ViewModel.HeadlessReplicas))
            {
                DispatcherQueue.TryEnqueue(() =>
                    HeadlessLaunchLabel.Text = ViewModel.HeadlessReplicas > 1
                        ? Helpers.Loc.F("Launch_LaunchReplicas", ViewModel.HeadlessReplicas)
                        : Helpers.Loc.T("Launch_LaunchJob"));
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

    private void OnGenerateHeadlessNameClick(object sender, RoutedEventArgs e)
    {
        ViewModel.GenerateHeadlessSessionName();
    }

    private void UpdateHeadlessAvailability()
    {
        var hasImages = ViewModel.HasHeadlessImages;
        NoHeadlessImagesBar.IsOpen = !hasImages;
        NoHeadlessImagesBar.Visibility = hasImages ? Visibility.Collapsed : Visibility.Visible;
        HeadlessProjectSection.Visibility = hasImages ? Visibility.Visible : Visibility.Collapsed;
        HeadlessImageSection.Visibility = hasImages ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnHeadlessResourceTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is RadioButtons rb && rb.SelectedItem is RadioButton selected)
        {
            // Writes the headless-only flag: this fires when the Pivot first realizes the tab, and
            // writing the shared ResourceType here would silently flip the Standard tab's launches
            // to flexible while its radios still display Fixed.
            var tag = selected.Tag?.ToString() ?? "flexible";
            ViewModel.HeadlessResourceType = tag;
            HeadlessResourcePanel.Visibility = tag == "fixed" ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OnHeadlessLaunchClick(object sender, RoutedEventArgs e)
    {
        ViewModel.Cores = HeadlessResourcePanel.Cores;
        ViewModel.Ram = HeadlessResourcePanel.Ram;
        ViewModel.Gpus = HeadlessResourcePanel.Gpus;

        HeadlessLaunchRequested?.Invoke(this, EventArgs.Empty);
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
            nameof(StdRegistryHelpBtn) => StdRegistryTip,
            nameof(StdProjectHelpBtn) => StdProjectTip,
            nameof(StdImageHelpBtn) => StdImageTip,
            nameof(StdNameHelpBtn) => StdNameTip,
            nameof(StdResTypeHelpBtn) => StdResTypeTip,
            nameof(AdvTypeHelpBtn) => AdvTypeTip,
            nameof(AdvImageHelpBtn) => AdvImageTip,
            nameof(AdvAuthHelpBtn) => AdvAuthTip,
            nameof(AdvNameHelpBtn) => AdvNameTip,
            nameof(AdvResTypeHelpBtn) => AdvResTypeTip,
            nameof(HlCmdHelpBtn) => HlCmdTip,
            nameof(HlArgsHelpBtn) => HlArgsTip,
            nameof(HlReplicasHelpBtn) => HlReplicasTip,
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
        HeadlessResourcePanel.Configure(coreOpts, ViewModel.Cores, ramOpts, ViewModel.Ram, gpuOpts);
        UpdateHeadlessAvailability();
    }
}
