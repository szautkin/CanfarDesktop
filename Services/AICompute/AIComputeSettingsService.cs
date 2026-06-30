using Windows.Security.Credentials;
using Windows.Storage;
using CanfarDesktop.Models.AICompute;
using CanfarDesktop.Services.ImageDiscovery;

namespace CanfarDesktop.Services.AICompute;

/// <summary>
/// Persists <see cref="AIComputeSettings"/> for the agent <c>run_code</c> tool: non-secret knobs
/// (compute image, cores, ram, registry host/username) in LocalSettings; the registry secret in the
/// Windows PasswordVault under a SEPARATE resource (<c>Verbinal.AICompute</c>) from Image Discovery, so
/// the two credential sets never collide. An empty image disables run_code. Mirrors
/// <see cref="ImageDiscoverySettingsService"/>.
/// </summary>
public class AIComputeSettingsService
{
    private const string KeyImage = "AICompute.Image";
    private const string KeyCores = "AICompute.Cores";
    private const string KeyRam = "AICompute.Ram";
    private const string KeyRegistryHost = "AICompute.RegistryHost";
    private const string KeyUsername = "AICompute.RegistryUsername";
    private const string VaultResource = "Verbinal.AICompute";

    private readonly ApplicationDataContainer? _localSettings;

    public AIComputeSettings Settings { get; private set; }

    public AIComputeSettingsService()
    {
        try { _localSettings = ApplicationData.Current.LocalSettings; }
        catch { _localSettings = null; }
        Settings = Load();
    }

    /// <summary>The configured compute image (trimmed); empty string when run_code is disabled.</summary>
    public string ResolveImage() => Settings.Image.Trim();

    /// <summary>The clamped (cores, ram) for the lazy compute launch.</summary>
    public (int Cores, int Ram) ResolveResources()
        => (RunCodeContract.ClampCores(Settings.Cores), RunCodeContract.ClampRam(Settings.Ram));

    /// <summary>The (username, secret) for the contributed-session registry auth, or empty when none.</summary>
    public (string Username, string Secret) RegistryCredentials()
        => (Settings.RegistryUsername, ReadSecret(Settings.RegistryHost, Settings.RegistryUsername) ?? string.Empty);

    /// <summary>Verify the stored credentials against the registry (Docker V2). <paramref name="client"/>
    /// must be a plain (non-auth'd) HttpClient — never the CADC token.</summary>
    public Task<RegistryTestResult> TestRegistryCredentialsAsync(HttpClient client)
    {
        var host = Settings.RegistryHost;
        var user = Settings.RegistryUsername;
        var secret = ReadSecret(host, user) ?? string.Empty;
        return RegistryCredentialTest.PerformAsync(host, user, secret, client);
    }

    public void SetImage(string value)
    {
        var final = value?.Trim() ?? string.Empty;
        WriteSetting(KeyImage, final);
        Settings = Settings with { Image = final };
    }

    public void SetCores(int value)
    {
        var final = RunCodeContract.ClampCores(value);
        WriteInt(KeyCores, final);
        Settings = Settings with { Cores = final };
    }

    public void SetRam(int value)
    {
        var final = RunCodeContract.ClampRam(value);
        WriteInt(KeyRam, final);
        Settings = Settings with { Ram = final };
    }

    public void SetRegistryHost(string value)
    {
        var final = string.IsNullOrWhiteSpace(value) ? AIComputeSettings.DefaultRegistryHost : value.Trim();
        WriteSetting(KeyRegistryHost, final);
        Settings = Settings with { RegistryHost = final, HasSecret = HasStoredSecret(final, Settings.RegistryUsername) };
    }

    public void SetUsername(string value)
    {
        var final = value?.Trim() ?? string.Empty;
        WriteSetting(KeyUsername, final);
        Settings = Settings with { RegistryUsername = final, HasSecret = HasStoredSecret(Settings.RegistryHost, final) };
    }

    public void SetSecret(string value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) { ClearSecret(); return; }
        if (string.IsNullOrEmpty(Settings.RegistryUsername))
            throw new InvalidOperationException("Set a registry username before storing a secret.");

        try
        {
            var vault = new PasswordVault();
            var account = Account(Settings.RegistryHost, Settings.RegistryUsername);
            RemoveExisting(vault, account);
            vault.Add(new PasswordCredential(VaultResource, account, trimmed));
            Settings = Settings with { HasSecret = true };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AICompute secret store failed: {ex.Message}");
        }
    }

    public void ClearSecret()
    {
        try
        {
            var vault = new PasswordVault();
            RemoveExisting(vault, Account(Settings.RegistryHost, Settings.RegistryUsername));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AICompute secret clear failed: {ex.Message}");
        }
        Settings = Settings with { HasSecret = false };
    }

    public void ResetToDefaults()
    {
        ClearSecret();
        if (_localSettings is not null)
        {
            _localSettings.Values.Remove(KeyImage);
            _localSettings.Values.Remove(KeyCores);
            _localSettings.Values.Remove(KeyRam);
            _localSettings.Values.Remove(KeyRegistryHost);
            _localSettings.Values.Remove(KeyUsername);
        }
        Settings = new AIComputeSettings();
    }

    private AIComputeSettings Load()
    {
        var host = ReadSetting(KeyRegistryHost) is { Length: > 0 } h ? h : AIComputeSettings.DefaultRegistryHost;
        var user = ReadSetting(KeyUsername) ?? string.Empty;
        return new AIComputeSettings
        {
            Image = ReadSetting(KeyImage) ?? string.Empty,
            Cores = ReadInt(KeyCores, RunCodeContract.DefaultCores),
            Ram = ReadInt(KeyRam, RunCodeContract.DefaultRam),
            RegistryHost = host,
            RegistryUsername = user,
            HasSecret = HasStoredSecret(host, user),
        };
    }

    private string? ReadSetting(string key)
        => _localSettings?.Values.TryGetValue(key, out var v) == true ? v as string : null;

    private void WriteSetting(string key, string value)
    {
        if (_localSettings is not null) _localSettings.Values[key] = value;
    }

    private int ReadInt(string key, int fallback)
        => _localSettings?.Values.TryGetValue(key, out var v) == true && v is int n ? n : fallback;

    private void WriteInt(string key, int value)
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
