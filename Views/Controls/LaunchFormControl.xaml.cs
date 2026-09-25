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
        NameHelpButtons();
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

    /// <summary>Each field's help button and the tip it opens.</summary>
    private Dictionary<Button, TeachingTip>? _helpTips;

    private Dictionary<Button, TeachingTip> HelpTips => _helpTips ??= new()
    {
        [StdTypeHelpBtn] = StdTypeTip,
        [StdRegistryHelpBtn] = StdRegistryTip,
        [StdProjectHelpBtn] = StdProjectTip,
        [StdImageHelpBtn] = StdImageTip,
        [StdNameHelpBtn] = StdNameTip,
        [StdResTypeHelpBtn] = StdResTypeTip,
        [AdvTypeHelpBtn] = AdvTypeTip,
        [AdvImageHelpBtn] = AdvImageTip,
        [AdvAuthHelpBtn] = AdvAuthTip,
        [AdvNameHelpBtn] = AdvNameTip,
        [AdvResTypeHelpBtn] = AdvResTypeTip,
        [HlCmdHelpBtn] = HlCmdTip,
        [HlArgsHelpBtn] = HlArgsTip,
        [HlReplicasHelpBtn] = HlReplicasTip,
    };

    /// <summary>
    /// A help button is a faint question mark with no words, so a screen reader announced fourteen
    /// unnamed buttons and an agent listing the form could point at one but not say what it was for.
    /// Each is named after the tip it opens, which is already translated.
    /// </summary>
    private void NameHelpButtons()
    {
        foreach (var (button, tip) in HelpTips)
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
                button, Helpers.Loc.F("Launch_HelpAbout", tip.Title));
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && HelpTips.TryGetValue(btn, out var tip))
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
