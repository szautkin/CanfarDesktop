using Windows.Storage;

namespace CanfarDesktop.Services;

public class SettingsService : ISettingsService
{
    private readonly ApplicationDataContainer? _localSettings;

    public SettingsService()
    {
        try
        {
            _localSettings = ApplicationData.Current.LocalSettings;
            Load();
        }
        catch
        {
            // Running unpackaged — ApplicationData not available, use defaults only
            _localSettings = null;
        }
    }

    public string ApiBaseUrl { get; set; } = "https://ws-cadc.canfar.net";
    public string DefaultSessionType { get; set; } = "notebook";
    public string DefaultResourceType { get; set; } = "none";
    public int DefaultCores { get; set; } = 2;
    public int DefaultRam { get; set; } = 8;
    public int DefaultGpus { get; set; }
    public string Theme { get; set; } = "System";
    public string Language { get; set; } = "system";

    public void Save()
    {
        if (_localSettings is null) return;
        _localSettings.Values["ApiBaseUrl"] = ApiBaseUrl;
        _localSettings.Values["DefaultSessionType"] = DefaultSessionType;
        _localSettings.Values["DefaultResourceType"] = DefaultResourceType;
        _localSettings.Values["DefaultCores"] = DefaultCores;
        _localSettings.Values["DefaultRam"] = DefaultRam;
        _localSettings.Values["DefaultGpus"] = DefaultGpus;
        _localSettings.Values["Theme"] = Theme;
        _localSettings.Values["Language"] = Language;
    }

    public void Load()
    {
        if (_localSettings is null) return;
        if (_localSettings.Values.TryGetValue("ApiBaseUrl", out var baseUrl))
            ApiBaseUrl = (string)baseUrl;
        if (_localSettings.Values.TryGetValue("DefaultSessionType", out var sessionType))
            DefaultSessionType = (string)sessionType;
        if (_localSettings.Values.TryGetValue("DefaultResourceType", out var resourceType))
            DefaultResourceType = (string)resourceType;
        if (_localSettings.Values.TryGetValue("DefaultCores", out var cores))
            DefaultCores = (int)cores;
        if (_localSettings.Values.TryGetValue("DefaultRam", out var ram))
            DefaultRam = (int)ram;
        if (_localSettings.Values.TryGetValue("DefaultGpus", out var gpus))
            DefaultGpus = (int)gpus;
        if (_localSettings.Values.TryGetValue("Theme", out var theme))
            Theme = (string)theme;
        if (_localSettings.Values.TryGetValue("Language", out var language))
            Language = (string)language;
    }
}
