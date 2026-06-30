using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.Services.AICompute;
using CanfarDesktop.Services.ImageDiscovery;

namespace CanfarDesktop.Views.Dialogs;

/// <summary>
/// Configures the agent <c>run_code</c> tool: the compute container image (empty ⇒ run_code disabled),
/// the instance size, and registry credentials to pull a private image (secret in the Windows
/// PasswordVault, under a resource separate from Image Discovery). Mirrors the Image Discovery dialog.
/// </summary>
public sealed partial class AIComputeSettingsDialog : ContentDialog
{
    private readonly AIComputeSettingsService _service;

    public AIComputeSettingsDialog(AIComputeSettingsService service)
    {
        InitializeComponent();
        _service = service;
        PrimaryButtonClick += OnSave;
        Populate();
    }

    public static Task ShowAsync(XamlRoot root)
    {
        var service = App.Services.GetRequiredService<AIComputeSettingsService>();
        return new AIComputeSettingsDialog(service) { XamlRoot = root }.ShowAsync().AsTask();
    }

    private void Populate()
    {
        var s = _service.Settings;
        ImageBox.Text = s.Image;
        CoresBox.Value = s.Cores;
        RamBox.Value = s.Ram;
        RegistryHostBox.Text = s.RegistryHost == Models.AICompute.AIComputeSettings.DefaultRegistryHost ? string.Empty : s.RegistryHost;
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
            ? "A secret is stored. Type a new one to replace it, or leave blank to keep it."
            : "No secret stored.";
        RemoveSecretButton.Visibility = hasSecret ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSave(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        _service.SetImage(ImageBox.Text);
        _service.SetCores(ToInt(CoresBox.Value, _service.Settings.Cores));
        _service.SetRam(ToInt(RamBox.Value, _service.Settings.Ram));
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
                args.Cancel = true;
            }
        }
    }

    private static int ToInt(double value, int fallback) => double.IsNaN(value) ? fallback : (int)value;

    private void OnRemoveSecret(object sender, RoutedEventArgs e)
    {
        _service.ClearSecret();
        SecretBox.Password = string.Empty;
        RefreshSecretStatus();
        ResetButton.IsEnabled = !_service.Settings.IsAllDefaults;
        ShowStatus(InfoBarSeverity.Success, "Secret removed", "The stored registry secret was deleted.");
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
        ShowStatus(InfoBarSeverity.Success, "Reset", "AI compute settings were reset to defaults (run_code is now disabled).");
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
            // A PLAIN factory client (never the CADC-auth'd one).
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
