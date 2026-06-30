using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.Helpers;
using CanfarDesktop.Services;

namespace CanfarDesktop.Views.Dialogs;

/// <summary>
/// The unified Settings window: a NavigationView consolidating the previously-scattered settings. General
/// (theme + defaults, wiring the app's <see cref="ISettingsService"/>) and About are embedded; the
/// specialized surfaces (AI agent / Image discovery / AI compute / Notebook) open from here — they stay
/// their own dialogs because WinUI allows only one ContentDialog at a time, so a launcher closes this one
/// first.
/// </summary>
public sealed partial class SettingsDialog : ContentDialog
{
    private readonly ISettingsService _settings;
    private bool _loading;

    public SettingsDialog()
    {
        InitializeComponent();
        _settings = App.Services.GetRequiredService<ISettingsService>();
        VersionText.Text = $"Version {AppVersion()}";
        PopulateGeneral();
        Nav.SelectedItem = Nav.MenuItems.Count > 0 ? Nav.MenuItems[0] : null; // General
    }

    public static Task ShowAsync(XamlRoot root)
        => new SettingsDialog { XamlRoot = root }.ShowAsync().AsTask();

    private void PopulateGeneral()
    {
        _loading = true;
        SelectByTag(ThemeCombo, _settings.Theme);
        SelectByTag(SessionTypeCombo, _settings.DefaultSessionType);
        CoresBox.Value = _settings.DefaultCores;
        RamBox.Value = _settings.DefaultRam;
        ApiBaseUrlBox.Text = _settings.ApiBaseUrl;
        _loading = false;
    }

    // ── nav ──

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag as string ?? "general";
        GeneralPanel.Visibility = Vis(tag == "general");
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

    private void OnGeneralChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.DefaultSessionType = SelectedTag(SessionTypeCombo) ?? _settings.DefaultSessionType;
        _settings.ApiBaseUrl = string.IsNullOrWhiteSpace(ApiBaseUrlBox.Text) ? _settings.ApiBaseUrl : ApiBaseUrlBox.Text.Trim();
        _settings.Save();
    }

    private void OnGeneralNumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading) return;
        if (!double.IsNaN(CoresBox.Value)) _settings.DefaultCores = (int)CoresBox.Value;
        if (!double.IsNaN(RamBox.Value)) _settings.DefaultRam = (int)RamBox.Value;
        _settings.Save();
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
