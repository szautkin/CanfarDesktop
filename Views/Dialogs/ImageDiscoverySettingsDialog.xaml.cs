using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.Models.ImageDiscovery;
using CanfarDesktop.Services.ImageDiscovery;

namespace CanfarDesktop.Views.Dialogs;

/// <summary>
/// Configures image discovery: the inspector host image, registry host/username/secret used to pull
/// private images (secret stored in the Windows PasswordVault), the discovery cache, and reset. When a
/// <see cref="ImageDiscoveryCoordinator"/> is supplied (the main-Settings entry point passes it) the
/// cache section is shown.
/// </summary>
public sealed partial class ImageDiscoverySettingsDialog : ContentDialog
{
    private readonly ImageDiscoverySettingsService _service;
    private readonly ImageDiscoveryCoordinator? _coordinator;

    public ImageDiscoverySettingsDialog(ImageDiscoverySettingsService service, ImageDiscoveryCoordinator? coordinator = null)
    {
        InitializeComponent();
        _service = service;
        _coordinator = coordinator;
        PrimaryButtonClick += OnSave;
        Populate();
    }

    /// <summary>Resolve the shared services from DI and show the dialog (the main-Settings entry point).</summary>
    public static Task ShowAsync(XamlRoot root)
    {
        var service = App.Services.GetRequiredService<ImageDiscoverySettingsService>();
        var coordinator = App.Services.GetService<ImageDiscoveryCoordinator>();
        return new ImageDiscoverySettingsDialog(service, coordinator) { XamlRoot = root }.ShowAsync().AsTask();
    }

    private void Populate()
    {
        var s = _service.Settings;
        // Show overrides only; blank fields mean "use the default".
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

    private void OnSave(ContentDialog sender, ContentDialogButtonClickEventArgs args)
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
            }
            catch (Exception ex)
            {
                ShowStatus(InfoBarSeverity.Error, "Couldn't save secret", ex.Message);
                args.Cancel = true; // keep the dialog open so the user can fix it
            }
        }
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

    /// <summary>
    /// Apply the typed host/username/secret, then probe the registry's Docker V2 token endpoint so
    /// the user can confirm the credentials work before a probe job fails later with ImagePullBackOff.
    /// </summary>
    private async void OnTestCredentials(object sender, RoutedEventArgs e)
    {
        // Test what the user typed: persist host/username and (if entered) the new secret first.
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
            // A PLAIN factory client (never the CADC-auth'd one) — the registry test must not leak the token.
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
