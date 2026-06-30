using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.Models.ImageDiscovery;
using CanfarDesktop.Services.ImageDiscovery;

namespace CanfarDesktop.Views.Dialogs;

/// <summary>
/// Image-discovery settings as a self-contained panel (inspector image, registry host/repository/
/// credentials, discovery cache, reset). Resolves its own services from DI so it can be hosted both by
/// the standalone <see cref="ImageDiscoverySettingsDialog"/> and embedded in the unified Settings window.
/// Persists via its own Save button.
/// </summary>
public sealed partial class ImageDiscoverySettingsPanel : UserControl
{
    private readonly ImageDiscoverySettingsService _service;
    private readonly ImageDiscoveryCoordinator? _coordinator;

    public ImageDiscoverySettingsPanel()
    {
        InitializeComponent();
        _service = App.Services.GetRequiredService<ImageDiscoverySettingsService>();
        _coordinator = App.Services.GetService<ImageDiscoveryCoordinator>();
        Populate();
    }

    private void Populate()
    {
        var s = _service.Settings;
        InspectorImageBox.Text = s.InspectorImage == ImageDiscoverySettings.DefaultInspectorImage ? string.Empty : s.InspectorImage;
        RegistryHostBox.Text = s.RegistryHost == ImageDiscoverySettings.DefaultRegistryHost ? string.Empty : s.RegistryHost;
        RegistryRepoBox.Text = s.RegistryRepository;
        UsernameBox.Text = s.Username;
        SecretBox.Password = string.Empty;
        RefreshSecretStatus();
        ResetConfirmPanel.Visibility = Visibility.Collapsed;
        ResetButton.IsEnabled = !s.IsAllDefaults;
        StatusBar.IsOpen = false;
        LoadCache();
    }

    private void RefreshSecretStatus()
    {
        var hasSecret = _service.Settings.HasSecret;
        SecretStatus.Text = hasSecret
            ? "A secret is stored. Type a new one to replace it, or leave blank to keep it."
            : "No secret stored.";
        RemoveSecretButton.Visibility = hasSecret ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LoadCache()
    {
        if (_coordinator is null)
        {
            CacheSection.Visibility = Visibility.Collapsed;
            return;
        }
        CacheSection.Visibility = Visibility.Visible;
        var count = _coordinator.CacheCount();
        CacheCountText.Text = count == 0
            ? "No images cached yet."
            : $"{count.ToString(CultureInfo.InvariantCulture)} image{(count == 1 ? "" : "s")} cached.";
        ClearCacheButton.IsEnabled = count > 0;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _service.SetInspectorImage(InspectorImageBox.Text);
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
                ShowStatus(InfoBarSeverity.Error, "Couldn't save secret", ex.Message);
                return;
            }
        }

        RefreshSecretStatus();
        ResetButton.IsEnabled = !_service.Settings.IsAllDefaults;
        ShowStatus(InfoBarSeverity.Success, "Saved", "Image-discovery settings were saved.");
    }

    private void OnRemoveSecret(object sender, RoutedEventArgs e)
    {
        _service.ClearSecret();
        SecretBox.Password = string.Empty;
        RefreshSecretStatus();
        ResetButton.IsEnabled = !_service.Settings.IsAllDefaults;
        ShowStatus(InfoBarSeverity.Success, "Secret removed", "The stored registry secret was deleted.");
    }

    private void OnClearCache(object sender, RoutedEventArgs e)
    {
        _coordinator?.ClearCache();
        LoadCache();
        ShowStatus(InfoBarSeverity.Success, "Cache cleared", "Discovered image manifests were cleared.");
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
        ShowStatus(InfoBarSeverity.Success, "Reset", "Image-discovery settings were reset to defaults.");
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
                ShowStatus(InfoBarSeverity.Error, "Couldn't save secret", ex.Message);
                return;
            }
        }

        TestButton.IsEnabled = false;
        ShowStatus(InfoBarSeverity.Informational, "Testing…", $"Contacting {_service.Settings.RegistryHost} …");
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
                RegistryTestKind.Success => "Credentials valid",
                RegistryTestKind.Unauthorized => "Credentials rejected",
                RegistryTestKind.MissingConfiguration => "Configuration incomplete",
                RegistryTestKind.InvalidChallenge => "Unexpected registry response",
                _ => "Network error",
            };
            ShowStatus(severity, title, result.Message);
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Test failed", ex.Message);
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
