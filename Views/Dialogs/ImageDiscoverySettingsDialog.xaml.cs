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
        SecretStatus.Text = s.HasSecret
            ? "A secret is stored. Type a new one to replace it, or leave blank to keep it."
            : "No secret stored.";
        ErrorBar.IsOpen = false;
    }

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
                ErrorBar.Message = ex.Message;
                ErrorBar.IsOpen = true;
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
}
