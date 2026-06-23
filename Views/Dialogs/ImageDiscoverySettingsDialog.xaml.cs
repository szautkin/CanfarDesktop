using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.Models.ImageDiscovery;
using CanfarDesktop.Services.ImageDiscovery;

namespace CanfarDesktop.Views.Dialogs;

/// <summary>
/// Configures image discovery: the inspector host image, and registry host/username/secret used to
/// pull private images (secret stored in the Windows PasswordVault).
/// </summary>
public sealed partial class ImageDiscoverySettingsDialog : ContentDialog
{
    private readonly ImageDiscoverySettingsService _service;

    public ImageDiscoverySettingsDialog(ImageDiscoverySettingsService service)
    {
        InitializeComponent();
        _service = service;
        PrimaryButtonClick += OnSave;
        SecondaryButtonClick += OnReset;
        Populate();
    }

    private void Populate()
    {
        var s = _service.Settings;
        // Show overrides only; blank fields mean "use the default".
        InspectorImageBox.Text = s.InspectorImage == ImageDiscoverySettings.DefaultInspectorImage ? string.Empty : s.InspectorImage;
        RegistryHostBox.Text = s.RegistryHost == ImageDiscoverySettings.DefaultRegistryHost ? string.Empty : s.RegistryHost;
        UsernameBox.Text = s.Username;
        SecretBox.Password = string.Empty;
        RefreshSecretStatus();
        StatusBar.IsOpen = false;
    }

    private void RefreshSecretStatus()
        => SecretStatus.Text = _service.Settings.HasSecret
            ? "A secret is stored. Type a new one to replace it, or leave blank to keep it."
            : "No secret stored.";

    private void OnSave(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        _service.SetInspectorImage(InspectorImageBox.Text);
        _service.SetRegistryHost(RegistryHostBox.Text);
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

    private void OnReset(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        _service.ResetToDefaults();
        Populate();
        args.Cancel = true; // keep open to show the reset state
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
            using var client = new HttpClient();
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
