using Windows.Security.Credentials;
using Windows.Storage;
using CanfarDesktop.Models.ImageDiscovery;

namespace CanfarDesktop.Services.ImageDiscovery;

/// <summary>
/// Persists <see cref="ImageDiscoverySettings"/>: non-secret knobs (registry host, username,
/// inspector image) in LocalSettings; the registry secret in the Windows PasswordVault keyed by
/// <c>host:username</c> (so multi-account users keep distinct credentials). Provides the inspector
/// image and the <c>x-skaha-registry-auth</c> header to the discovery coordinator.
/// </summary>
public class ImageDiscoverySettingsService
{
    private const string KeyRegistryHost = "ImageDiscovery.RegistryHost";
    private const string KeyUsername = "ImageDiscovery.Username";
    private const string KeyInspectorImage = "ImageDiscovery.InspectorImage";
    private const string VaultResource = "Verbinal.ImageDiscovery";

    private readonly ApplicationDataContainer? _localSettings;

    public ImageDiscoverySettings Settings { get; private set; }

    public ImageDiscoverySettingsService()
    {
        try { _localSettings = ApplicationData.Current.LocalSettings; }
        catch { _localSettings = null; }
        Settings = Load();
    }

    /// <summary>The inspector host image to launch (user override, or the built-in default).</summary>
    public string ResolveInspectorImage() => Settings.InspectorImage;

    /// <summary>The <c>x-skaha-registry-auth</c> header value, or null when no credentials are configured.</summary>
    public string? CurrentAuthHeader()
    {
        if (string.IsNullOrEmpty(Settings.Username)) return null;
        var secret = ReadSecret(Settings.RegistryHost, Settings.Username);
        return string.IsNullOrEmpty(secret) ? null : ImageDiscoverySettings.BuildAuthHeader(Settings.Username, secret);
    }

    public void SetInspectorImage(string value)
    {
        var final = string.IsNullOrWhiteSpace(value) ? ImageDiscoverySettings.DefaultInspectorImage : value.Trim();
        WriteSetting(KeyInspectorImage, final);
        Settings = Settings with { InspectorImage = final };
    }

    public void SetRegistryHost(string value)
    {
        var final = string.IsNullOrWhiteSpace(value) ? ImageDiscoverySettings.DefaultRegistryHost : value.Trim();
        WriteSetting(KeyRegistryHost, final);
        Settings = Settings with { RegistryHost = final, HasSecret = HasStoredSecret(final, Settings.Username) };
    }

    public void SetUsername(string value)
    {
        var final = value?.Trim() ?? string.Empty;
        WriteSetting(KeyUsername, final);
        Settings = Settings with { Username = final, HasSecret = HasStoredSecret(Settings.RegistryHost, final) };
    }

    /// <summary>Store (or, when empty, clear) the registry secret for the current host+username.</summary>
    public void SetSecret(string value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) { ClearSecret(); return; }
        if (string.IsNullOrEmpty(Settings.Username))
            throw new InvalidOperationException("Set a registry username before storing a secret.");

        try
        {
            var vault = new PasswordVault();
            var account = Account(Settings.RegistryHost, Settings.Username);
            RemoveExisting(vault, account);
            vault.Add(new PasswordCredential(VaultResource, account, trimmed));
            Settings = Settings with { HasSecret = true };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ImageDiscovery secret store failed: {ex.Message}");
        }
    }

    public void ClearSecret()
    {
        try
        {
            var vault = new PasswordVault();
            RemoveExisting(vault, Account(Settings.RegistryHost, Settings.Username));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ImageDiscovery secret clear failed: {ex.Message}");
        }
        Settings = Settings with { HasSecret = false };
    }

    public void ResetToDefaults()
    {
        ClearSecret();
        if (_localSettings is not null)
        {
            _localSettings.Values.Remove(KeyRegistryHost);
            _localSettings.Values.Remove(KeyUsername);
            _localSettings.Values.Remove(KeyInspectorImage);
        }
        Settings = new ImageDiscoverySettings();
    }

    private ImageDiscoverySettings Load()
    {
        var host = ReadSetting(KeyRegistryHost) is { Length: > 0 } h ? h : ImageDiscoverySettings.DefaultRegistryHost;
        var user = ReadSetting(KeyUsername) ?? string.Empty;
        var image = ReadSetting(KeyInspectorImage) is { Length: > 0 } i ? i : ImageDiscoverySettings.DefaultInspectorImage;
        return new ImageDiscoverySettings
        {
            RegistryHost = host,
            Username = user,
            InspectorImage = image,
            HasSecret = HasStoredSecret(host, user),
        };
    }

    private string? ReadSetting(string key)
        => _localSettings?.Values.TryGetValue(key, out var v) == true ? v as string : null;

    private void WriteSetting(string key, string value)
    {
        if (_localSettings is not null) _localSettings.Values[key] = value;
    }

    private bool HasStoredSecret(string host, string username)
        => !string.IsNullOrEmpty(ReadSecret(host, username));

    private static string Account(string host, string username) => $"{host}:{username}";

    private static string? ReadSecret(string host, string username)
    {
        if (string.IsNullOrEmpty(username)) return null;
        try
        {
            var vault = new PasswordVault();
            var cred = vault.Retrieve(VaultResource, Account(host, username));
            cred.RetrievePassword();
            return cred.Password;
        }
        catch
        {
            return null; // not found / unpackaged
        }
    }

    private static void RemoveExisting(PasswordVault vault, string account)
    {
        try { vault.Remove(vault.Retrieve(VaultResource, account)); }
        catch { /* nothing stored */ }
    }
}
