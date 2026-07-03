using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.Helpers;
using CanfarDesktop.Services;

namespace CanfarDesktop.Views.Dialogs;

/// <summary>
/// The unified Settings window: a NavigationView consolidating the previously-scattered settings. General
/// (theme + API base URL), Portal (session-launch defaults; both wire the app's
/// <see cref="ISettingsService"/>) and About are embedded; the
/// specialized surfaces (AI agent / Image discovery / AI compute / Notebook) open from here — they stay
/// their own dialogs because WinUI allows only one ContentDialog at a time, so a launcher closes this one
/// first.
/// </summary>
public sealed partial class SettingsDialog : ContentDialog
{
    private readonly ISettingsService _settings;
    private bool _loading;
    private Func<Task>? _showTerms;

    public SettingsDialog()
    {
        InitializeComponent();
        _settings = App.Services.GetRequiredService<ISettingsService>();
        VersionText.Text = Loc.F("About_Version", AppVersion());
        AboutSubtitleText.Text = Loc.T("About_Subtitle");
        PopulateGeneral();
        PopulatePortal();
        Nav.SelectedItem = Nav.MenuItems.Count > 0 ? Nav.MenuItems[0] : null; // General

        // Flush pending edits when the dialog closes (Escape or the close button
        // would otherwise silently discard General text that hasn't lost focus yet
        // and unsaved edits in the Save-button panels).
        Closing += (_, _) =>
        {
            SaveGeneral();
            SavePortal();
            if (ComputeSettingsPanel.IsDirty) ComputeSettingsPanel.SaveNow();
            if (DiscoverySettingsPanel.IsDirty) DiscoverySettingsPanel.SaveNow();
        };
    }

    public static Task ShowAsync(XamlRoot root, Func<Task>? showTerms = null)
        => new SettingsDialog { XamlRoot = root, _showTerms = showTerms }.ShowAsync().AsTask();

    // Only one ContentDialog may be open at a time — close Settings before the Terms viewer.
    private async void OnTermsLinkClick(object sender, RoutedEventArgs e)
    {
        Hide();
        if (_showTerms is not null) await _showTerms();
    }

    private void PopulateGeneral()
    {
        _loading = true;
        SelectByTag(ThemeCombo, _settings.Theme);
        SelectByTag(LanguageCombo, _settings.Language);
        PopulateEndpoints();
        _loading = false;
    }

    private void PopulateEndpoints()
    {
        EpLoginBox.Text = _settings.EndpointLoginBase;
        EpSkahaBox.Text = _settings.EndpointSkahaBase;
        EpAcBox.Text = _settings.EndpointAcBase;
        EpArcNodesBox.Text = _settings.EndpointArcNodes;
        EpArcFilesBox.Text = _settings.EndpointArcFiles;
        EpTapBox.Text = _settings.EndpointTapBase;
        EpCaom2OpsBox.Text = _settings.EndpointCaom2OpsBase;
        EpResolverBox.Text = _settings.EndpointResolverBase;
    }

    private void OnResetEndpointsClick(object sender, RoutedEventArgs e)
    {
        _settings.ResetEndpoints();
        _loading = true;
        PopulateEndpoints();
        _loading = false;
        SaveGeneral();
    }

    private void PopulatePortal()
    {
        _loading = true;
        SelectByTag(SessionTypeCombo, _settings.DefaultSessionType);
        SelectByTag(ResourcePresetCombo, _settings.DefaultResourceType);
        CoresBox.Value = _settings.DefaultCores;
        RamBox.Value = _settings.DefaultRam;
        GpusBox.Value = _settings.DefaultGpus;
        UpdateFixedResourceVisibility();
        _loading = false;
    }

    // ── nav ──

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag as string ?? "general";
        GeneralPanel.Visibility = Vis(tag == "general");
        PortalPanel.Visibility = Vis(tag == "portal");
        AgentPanel.Visibility = Vis(tag == "agent");
        DiscoveryPanel.Visibility = Vis(tag == "discovery");
        ComputePanel.Visibility = Vis(tag == "compute");
        NotebookPanel.Visibility = Vis(tag == "notebook");
        AboutPanel.Visibility = Vis(tag == "about");
    }

    private static Visibility Vis(bool show) => show ? Visibility.Visible : Visibility.Collapsed;

    // ── General persistence (auto-save) ──

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _settings.Theme = SelectedTag(ThemeCombo) ?? "System";
        _settings.Save();
        ThemeApplier.Apply(XamlRoot?.Content as FrameworkElement, _settings.Theme);
    }

    private void OnGeneralChanged(object sender, RoutedEventArgs e) => SaveGeneral();

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        SaveGeneral();
        ApplyLanguageOverride(_settings.Language);
        LanguageRestartBar.IsOpen = true; // offer the restart instead of just stating the need
    }

    private void OnRestartNowClick(object sender, RoutedEventArgs e)
    {
        // Flush pending edits first — Restart() does not run the Closing handler.
        SaveGeneral();
        SavePortal();
        if (ComputeSettingsPanel.IsDirty) ComputeSettingsPanel.SaveNow();
        if (DiscoverySettingsPanel.IsDirty) DiscoverySettingsPanel.SaveNow();

        // Returns only on failure (on success the process is replaced).
        var reason = Microsoft.Windows.AppLifecycle.AppInstance.Restart(string.Empty);
        LanguageRestartBar.Severity = InfoBarSeverity.Warning;
        LanguageRestartBar.Message = Loc.F("Settings_RestartFailed", reason.ToString());
    }

    private void SaveGeneral()
    {
        if (_loading) return;
        _settings.Language = SelectedTag(LanguageCombo) ?? _settings.Language;

        static string Keep(string edited, string current)
            => string.IsNullOrWhiteSpace(edited) ? current : edited.Trim();
        _settings.EndpointLoginBase = Keep(EpLoginBox.Text, _settings.EndpointLoginBase);
        _settings.EndpointSkahaBase = Keep(EpSkahaBox.Text, _settings.EndpointSkahaBase);
        _settings.EndpointAcBase = Keep(EpAcBox.Text, _settings.EndpointAcBase);
        _settings.EndpointArcNodes = Keep(EpArcNodesBox.Text, _settings.EndpointArcNodes);
        _settings.EndpointArcFiles = Keep(EpArcFilesBox.Text, _settings.EndpointArcFiles);
        _settings.EndpointTapBase = Keep(EpTapBox.Text, _settings.EndpointTapBase);
        _settings.EndpointCaom2OpsBase = Keep(EpCaom2OpsBox.Text, _settings.EndpointCaom2OpsBase);
        _settings.EndpointResolverBase = Keep(EpResolverBox.Text, _settings.EndpointResolverBase);

        _settings.Save();

        // Live-apply: URLs are built per request, so the next call already uses the new hosts.
        try { _settings.ApplyEndpointsTo(App.Services.GetRequiredService<Helpers.ApiEndpoints>()); }
        catch { /* endpoints singleton unavailable in test slices */ }
    }

    /// <summary>
    /// Persist the override with Windows too, so the next launch starts in the chosen language even
    /// before App reads the setting. Loaded UI keeps its current language until restart (macOS parity).
    /// </summary>
    private static void ApplyLanguageOverride(string language)
    {
        try
        {
            Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride =
                language switch { "en" => "en-US", "fr" => "fr-FR", _ => "" };
        }
        catch { /* unpackaged run — override unavailable; App applies the setting next launch */ }
    }

    // ── Portal persistence (auto-save; macOS Portal settings tab parity) ──

    private void OnPortalChanged(object sender, RoutedEventArgs e) => SavePortal();

    private void OnPortalNumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => SavePortal();

    private void OnResourcePresetChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateFixedResourceVisibility();
        SavePortal();
    }

    // Cores/RAM/GPUs only apply to the "fixed" preset (macOS shows the value pickers only then).
    private void UpdateFixedResourceVisibility()
        => FixedResourcePanel.Visibility = Vis(SelectedTag(ResourcePresetCombo) == "fixed");

    private void SavePortal()
    {
        if (_loading) return;
        _settings.DefaultSessionType = SelectedTag(SessionTypeCombo) ?? _settings.DefaultSessionType;
        _settings.DefaultResourceType = SelectedTag(ResourcePresetCombo) ?? _settings.DefaultResourceType;
        if (!double.IsNaN(CoresBox.Value)) _settings.DefaultCores = (int)CoresBox.Value;
        if (!double.IsNaN(RamBox.Value)) _settings.DefaultRam = (int)RamBox.Value;
        if (!double.IsNaN(GpusBox.Value)) _settings.DefaultGpus = (int)GpusBox.Value;
        _settings.Save();
    }

    private void OnClearPortalDefaults(object sender, RoutedEventArgs e)
    {
        // macOS "Clear All Defaults": back to the built-in launch-form defaults.
        _settings.DefaultSessionType = "notebook";
        _settings.DefaultResourceType = "none";
        _settings.DefaultCores = 2;
        _settings.DefaultRam = 8;
        _settings.DefaultGpus = 0;
        _settings.Save();
        PopulatePortal();
    }

    // ── launchers (close this dialog first — only one ContentDialog may be open) ──

    // The guided wizard is still a launcher (it's a ContentDialog; only one may be open at a time).
    private async void OnOpenWizard(object sender, RoutedEventArgs e) => await OpenAsync(AiConnectWizardDialog.ShowAsync);

    private async Task OpenAsync(Func<XamlRoot, Task> open)
    {
        var root = XamlRoot;
        Hide();
        if (root is not null) await open(root);
    }

    // ── helpers ──

    private static void SelectByTag(ComboBox combo, string? tag)
    {
        foreach (var obj in combo.Items)
            if (obj is ComboBoxItem item && string.Equals((string?)item.Tag, tag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private static string? SelectedTag(ComboBox combo) => (combo.SelectedItem as ComboBoxItem)?.Tag as string;

    private static string AppVersion()
    {
        try
        {
            var v = Windows.ApplicationModel.Package.Current.Id.Version;
            return $"{v.Major}.{v.Minor}.{v.Build}";
        }
        catch
        {
            return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0";
        }
    }
}
