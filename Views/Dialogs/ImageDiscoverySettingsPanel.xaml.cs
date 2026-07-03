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
            ? Helpers.Loc.T("Discovery_SecretStored")
            : Helpers.Loc.T("Discovery_NoSecret");
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
            ? Helpers.Loc.T("Discovery_CacheEmpty")
            : count == 1
                ? Helpers.Loc.T("Discovery_CacheCountOne")
                : Helpers.Loc.F("Discovery_CacheCountMany", count);
        ClearCacheButton.IsEnabled = count > 0;
    }

    /// <summary>True when the UI holds edits that haven't been saved yet.</summary>
    public bool IsDirty
    {
        get
        {
            var s = _service.Settings;
            var imageShown = s.InspectorImage == ImageDiscoverySettings.DefaultInspectorImage
                ? string.Empty : s.InspectorImage;
            var hostShown = s.RegistryHost == ImageDiscoverySettings.DefaultRegistryHost
                ? string.Empty : s.RegistryHost;
            return InspectorImageBox.Text != imageShown
                || RegistryHostBox.Text != hostShown
                || RegistryRepoBox.Text != s.RegistryRepository
                || UsernameBox.Text != s.Username
                || SecretBox.Password.Length > 0;
        }
    }

    /// <summary>Persist pending edits — the host Settings dialog flushes on close.</summary>
    public void SaveNow() => OnSave(this, new RoutedEventArgs());

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
                ShowStatus(InfoBarSeverity.Error, Helpers.Loc.T("Discovery_SecretSaveFailedTitle"), ex.Message);
                return;
            }
        }

        RefreshSecretStatus();
        ResetButton.IsEnabled = !_service.Settings.IsAllDefaults;
        ShowStatus(InfoBarSeverity.Success, Helpers.Loc.T("Discovery_SavedTitle"), Helpers.Loc.T("Discovery_SavedBody"));
    }

    private void OnRemoveSecret(object sender, RoutedEventArgs e)
    {
        _service.ClearSecret();
        SecretBox.Password = string.Empty;
        RefreshSecretStatus();
        ResetButton.IsEnabled = !_service.Settings.IsAllDefaults;
        ShowStatus(InfoBarSeverity.Success, Helpers.Loc.T("Discovery_SecretRemovedTitle"), Helpers.Loc.T("Discovery_SecretRemovedBody"));
    }

    private void OnClearCache(object sender, RoutedEventArgs e)
    {
        _coordinator?.ClearCache();
        LoadCache();
        ShowStatus(InfoBarSeverity.Success, Helpers.Loc.T("Discovery_CacheClearedTitle"), Helpers.Loc.T("Discovery_CacheClearedBody"));
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
        ShowStatus(InfoBarSeverity.Success, Helpers.Loc.T("Discovery_ResetTitle"), Helpers.Loc.T("Discovery_ResetBody"));
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
                ShowStatus(InfoBarSeverity.Error, Helpers.Loc.T("Discovery_SecretSaveFailedTitle"), ex.Message);
                return;
            }
        }

        TestButton.IsEnabled = false;
        ShowStatus(InfoBarSeverity.Informational, Helpers.Loc.T("Discovery_TestingTitle"), Helpers.Loc.F("Discovery_ContactingBody", _service.Settings.RegistryHost));
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
                RegistryTestKind.Success => Helpers.Loc.T("Discovery_TestValid"),
                RegistryTestKind.Unauthorized => Helpers.Loc.T("Discovery_TestRejected"),
                RegistryTestKind.MissingConfiguration => Helpers.Loc.T("Discovery_TestIncomplete"),
                RegistryTestKind.InvalidChallenge => Helpers.Loc.T("Discovery_TestUnexpected"),
                _ => Helpers.Loc.T("Discovery_TestNetworkError"),
            };
            ShowStatus(severity, title, result.Message);
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, Helpers.Loc.T("Discovery_TestFailed"), ex.Message);
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
