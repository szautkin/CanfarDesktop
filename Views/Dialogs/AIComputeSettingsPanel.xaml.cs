using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.Services.AICompute;
using CanfarDesktop.Services.ImageDiscovery;

namespace CanfarDesktop.Views.Dialogs;

/// <summary>
/// AI-compute settings as a self-contained panel (compute image, instance size, registry credentials).
/// Resolves its own service from DI so it can be hosted both by the standalone
/// <see cref="AIComputeSettingsDialog"/> and embedded in the unified Settings window. Persists via Save.
/// </summary>
public sealed partial class AIComputeSettingsPanel : UserControl
{
    private readonly AIComputeSettingsService _service;

    public AIComputeSettingsPanel()
    {
        InitializeComponent();
        _service = App.Services.GetRequiredService<AIComputeSettingsService>();
        Populate();
    }

    private void Populate()
    {
        var s = _service.Settings;
        ImageBox.Text = s.Image;
        CoresBox.Value = s.Cores;
        RamBox.Value = s.Ram;
        RegistryHostBox.Text = s.RegistryHost == Models.AICompute.AIComputeSettings.DefaultRegistryHost ? string.Empty : s.RegistryHost;
        RegistryRepoBox.Text = s.RegistryRepository;
        UsernameBox.Text = s.RegistryUsername;
        SecretBox.Password = string.Empty;
        RefreshSecretStatus();
        ResetConfirmPanel.Visibility = Visibility.Collapsed;
        ResetButton.IsEnabled = !s.IsAllDefaults;
        StatusBar.IsOpen = false;
    }

    private void RefreshSecretStatus()
    {
        var hasSecret = _service.Settings.HasSecret;
        SecretStatus.Text = hasSecret
            ? Helpers.Loc.T("Compute_SecretStored")
            : Helpers.Loc.T("Compute_NoSecret");
        RemoveSecretButton.Visibility = hasSecret ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>True when the UI holds edits that haven't been saved yet.</summary>
    public bool IsDirty
    {
        get
        {
            var s = _service.Settings;
            var hostShown = s.RegistryHost == Models.AICompute.AIComputeSettings.DefaultRegistryHost
                ? string.Empty : s.RegistryHost;
            return ImageBox.Text != s.Image
                || ToInt(CoresBox.Value, s.Cores) != s.Cores
                || ToInt(RamBox.Value, s.Ram) != s.Ram
                || RegistryHostBox.Text != hostShown
                || RegistryRepoBox.Text != s.RegistryRepository
                || UsernameBox.Text != s.RegistryUsername
                || SecretBox.Password.Length > 0;
        }
    }

    /// <summary>Persist pending edits — the host Settings dialog flushes on close.</summary>
    public void SaveNow() => OnSave(this, new RoutedEventArgs());

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _service.SetImage(ImageBox.Text);
        _service.SetCores(ToInt(CoresBox.Value, _service.Settings.Cores));
        _service.SetRam(ToInt(RamBox.Value, _service.Settings.Ram));
        _service.SetRegistryHost(RegistryHostBox.Text);
        _service.SetRegistryRepository(RegistryRepoBox.Text);
        _service.SetUsername(UsernameBox.Text);

        if (SecretBox.Password.Length > 0)
        {
            try
            {
                _service.SetSecret(SecretBox.Password);
                SecretBox.Password = string.Empty;
            }
            catch (Exception ex)
            {
                ShowStatus(InfoBarSeverity.Error, Helpers.Loc.T("Compute_SecretSaveFailedTitle"), ex.Message);
                return;
            }
        }

        RefreshSecretStatus();
        ResetButton.IsEnabled = !_service.Settings.IsAllDefaults;
        ShowStatus(InfoBarSeverity.Success, Helpers.Loc.T("Compute_SavedTitle"), Helpers.Loc.T("Compute_SavedBody"));
    }

    private static int ToInt(double value, int fallback) => double.IsNaN(value) ? fallback : (int)value;

    private void OnRemoveSecret(object sender, RoutedEventArgs e)
    {
        _service.ClearSecret();
        SecretBox.Password = string.Empty;
        RefreshSecretStatus();
        ResetButton.IsEnabled = !_service.Settings.IsAllDefaults;
        ShowStatus(InfoBarSeverity.Success, Helpers.Loc.T("Compute_SecretRemovedTitle"), Helpers.Loc.T("Compute_SecretRemovedBody"));
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        StatusBar.IsOpen = false;
        ResetConfirmPanel.Visibility = Visibility.Visible;
    }

    private void OnResetCancel(object sender, RoutedEventArgs e)
        => ResetConfirmPanel.Visibility = Visibility.Collapsed;

    private void OnResetConfirm(object sender, RoutedEventArgs e)
    {
        _service.ResetToDefaults();
        Populate();
        ShowStatus(InfoBarSeverity.Success, Helpers.Loc.T("Compute_ResetTitle"), Helpers.Loc.T("Compute_ResetBody"));
    }

    private async void OnTestCredentials(object sender, RoutedEventArgs e)
    {
        _service.SetRegistryHost(RegistryHostBox.Text);
        _service.SetUsername(UsernameBox.Text);
        if (SecretBox.Password.Length > 0)
        {
            try
            {
                _service.SetSecret(SecretBox.Password);
                SecretBox.Password = string.Empty;
                RefreshSecretStatus();
            }
            catch (Exception ex)
            {
                ShowStatus(InfoBarSeverity.Error, Helpers.Loc.T("Compute_SecretSaveFailedTitle"), ex.Message);
                return;
            }
        }

        TestButton.IsEnabled = false;
        ShowStatus(InfoBarSeverity.Informational, Helpers.Loc.T("Compute_TestingTitle"), Helpers.Loc.F("Compute_ContactingBody", _service.Settings.RegistryHost));
        try
        {
            var client = App.Services.GetRequiredService<IHttpClientFactory>().CreateClient();
            var result = await _service.TestRegistryCredentialsAsync(client);
            var severity = result.Kind switch
            {
                RegistryTestKind.Success => InfoBarSeverity.Success,
                RegistryTestKind.Unauthorized or RegistryTestKind.MissingConfiguration => InfoBarSeverity.Warning,
                _ => InfoBarSeverity.Error,
            };
            var title = result.Kind switch
            {
                RegistryTestKind.Success => Helpers.Loc.T("Compute_TestValid"),
                RegistryTestKind.Unauthorized => Helpers.Loc.T("Compute_TestRejected"),
                RegistryTestKind.MissingConfiguration => Helpers.Loc.T("Compute_TestIncomplete"),
                RegistryTestKind.InvalidChallenge => Helpers.Loc.T("Compute_TestUnexpected"),
                _ => Helpers.Loc.T("Compute_TestNetworkError"),
            };
            ShowStatus(severity, title, result.Message);
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, Helpers.Loc.T("Compute_TestFailed"), ex.Message);
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    private void ShowStatus(InfoBarSeverity severity, string title, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Title = title;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }
}
