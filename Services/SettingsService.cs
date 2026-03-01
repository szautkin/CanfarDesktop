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
    public int DefaultCores { get; set; } = 2;
    public int DefaultRam { get; set; } = 8;
    public string Theme { get; set; } = "System";

    public void Save()
    {
        if (_localSettings is null) return;
        _localSettings.Values["ApiBaseUrl"] = ApiBaseUrl;
        _localSettings.Values["DefaultSessionType"] = DefaultSessionType;
        _localSettings.Values["DefaultCores"] = DefaultCores;
        _localSettings.Values["DefaultRam"] = DefaultRam;
        _localSettings.Values["Theme"] = Theme;
    }

    public void Load()
    {
        if (_localSettings is null) return;
        if (_localSettings.Values.TryGetValue("ApiBaseUrl", out var baseUrl))
            ApiBaseUrl = (string)baseUrl;
        if (_localSettings.Values.TryGetValue("DefaultSessionType", out var sessionType))
            DefaultSessionType = (string)sessionType;
        if (_localSettings.Values.TryGetValue("DefaultCores", out var cores))
            DefaultCores = (int)cores;
        if (_localSettings.Values.TryGetValue("DefaultRam", out var ram))
            DefaultRam = (int)ram;
        if (_localSettings.Values.TryGetValue("Theme", out var theme))
            Theme = (string)theme;
    }
}
