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

    // Endpoint defaults come from Helpers.ApiEndpointDefaults — the single source of truth
    // shared with ApiEndpoints' initializers. Values equal to the default are NOT persisted, so a
    // future release changing a default reaches every user who never customized that field.
    public string EndpointLoginBase { get; set; } = Helpers.ApiEndpointDefaults.LoginBase;
    public string EndpointSkahaBase { get; set; } = Helpers.ApiEndpointDefaults.SkahaBase;
    public string EndpointAcBase { get; set; } = Helpers.ApiEndpointDefaults.AcBase;
    public string EndpointArcNodes { get; set; } = Helpers.ApiEndpointDefaults.ArcNodes;
    public string EndpointArcFiles { get; set; } = Helpers.ApiEndpointDefaults.ArcFiles;
    public string EndpointTapBase { get; set; } = Helpers.ApiEndpointDefaults.TapBase;
    public string EndpointCaom2OpsBase { get; set; } = Helpers.ApiEndpointDefaults.Caom2OpsBase;
    public string EndpointResolverBase { get; set; } = Helpers.ApiEndpointDefaults.ResolverBase;

    public void ResetEndpoints()
    {
        EndpointLoginBase = Helpers.ApiEndpointDefaults.LoginBase;
        EndpointSkahaBase = Helpers.ApiEndpointDefaults.SkahaBase;
        EndpointAcBase = Helpers.ApiEndpointDefaults.AcBase;
        EndpointArcNodes = Helpers.ApiEndpointDefaults.ArcNodes;
        EndpointArcFiles = Helpers.ApiEndpointDefaults.ArcFiles;
        EndpointTapBase = Helpers.ApiEndpointDefaults.TapBase;
        EndpointCaom2OpsBase = Helpers.ApiEndpointDefaults.Caom2OpsBase;
        EndpointResolverBase = Helpers.ApiEndpointDefaults.ResolverBase;
    }

    public void ApplyEndpointsTo(Helpers.ApiEndpoints endpoints)
    {
        static string Clean(string value, string fallback)
        {
            var v = (value ?? string.Empty).Trim().TrimEnd('/');
            return Uri.TryCreate(v, UriKind.Absolute, out var uri)
                   && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                ? v : fallback; // a malformed host must never brick the app — fall back to default
        }

        endpoints.LoginBaseUrl = Clean(EndpointLoginBase, Helpers.ApiEndpointDefaults.LoginBase);
        endpoints.SkahaBaseUrl = Clean(EndpointSkahaBase, Helpers.ApiEndpointDefaults.SkahaBase);
        endpoints.AcBaseUrl = Clean(EndpointAcBase, Helpers.ApiEndpointDefaults.AcBase);
        endpoints.ArcNodesRoot = Clean(EndpointArcNodes, Helpers.ApiEndpointDefaults.ArcNodes);
        endpoints.ArcFilesRoot = Clean(EndpointArcFiles, Helpers.ApiEndpointDefaults.ArcFiles);
        endpoints.TapBaseUrl = Clean(EndpointTapBase, Helpers.ApiEndpointDefaults.TapBase);
        endpoints.Caom2OpsBaseUrl = Clean(EndpointCaom2OpsBase, Helpers.ApiEndpointDefaults.Caom2OpsBase);
        endpoints.ResolverBaseUrl = Clean(EndpointResolverBase, Helpers.ApiEndpointDefaults.ResolverBase);
    }

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
        _localSettings.Values["DefaultSessionType"] = DefaultSessionType;
        _localSettings.Values["DefaultResourceType"] = DefaultResourceType;
        _localSettings.Values["DefaultCores"] = DefaultCores;
        _localSettings.Values["DefaultRam"] = DefaultRam;
        _localSettings.Values["DefaultGpus"] = DefaultGpus;
        _localSettings.Values["Theme"] = Theme;
        _localSettings.Values["Language"] = Language;
        // Endpoints: persist only real customizations. Freezing the current defaults into
        // settings would pin every user to this release's hosts forever.
        SaveEndpoint("EndpointLoginBase", EndpointLoginBase, Helpers.ApiEndpointDefaults.LoginBase);
        SaveEndpoint("EndpointSkahaBase", EndpointSkahaBase, Helpers.ApiEndpointDefaults.SkahaBase);
        SaveEndpoint("EndpointAcBase", EndpointAcBase, Helpers.ApiEndpointDefaults.AcBase);
        SaveEndpoint("EndpointArcNodes", EndpointArcNodes, Helpers.ApiEndpointDefaults.ArcNodes);
        SaveEndpoint("EndpointArcFiles", EndpointArcFiles, Helpers.ApiEndpointDefaults.ArcFiles);
        SaveEndpoint("EndpointTapBase", EndpointTapBase, Helpers.ApiEndpointDefaults.TapBase);
        SaveEndpoint("EndpointCaom2OpsBase", EndpointCaom2OpsBase, Helpers.ApiEndpointDefaults.Caom2OpsBase);
        SaveEndpoint("EndpointResolverBase", EndpointResolverBase, Helpers.ApiEndpointDefaults.ResolverBase);
    }


    private void SaveEndpoint(string key, string value, string defaultValue)
    {
        if (_localSettings is null) return;
        if (string.Equals(value, defaultValue, StringComparison.Ordinal))
            _localSettings.Values.Remove(key);
        else
            _localSettings.Values[key] = value;
    }

    public void Load()
    {
        if (_localSettings is null) return;
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
        if (_localSettings.Values.TryGetValue("EndpointLoginBase", out var e1)) EndpointLoginBase = (string)e1;
        if (_localSettings.Values.TryGetValue("EndpointSkahaBase", out var e2)) EndpointSkahaBase = (string)e2;
        if (_localSettings.Values.TryGetValue("EndpointAcBase", out var e3)) EndpointAcBase = (string)e3;
        if (_localSettings.Values.TryGetValue("EndpointArcNodes", out var e4)) EndpointArcNodes = (string)e4;
        if (_localSettings.Values.TryGetValue("EndpointArcFiles", out var e5)) EndpointArcFiles = (string)e5;
        if (_localSettings.Values.TryGetValue("EndpointTapBase", out var e6)) EndpointTapBase = (string)e6;
        if (_localSettings.Values.TryGetValue("EndpointCaom2OpsBase", out var e7)) EndpointCaom2OpsBase = (string)e7;
        if (_localSettings.Values.TryGetValue("EndpointResolverBase", out var e8)) EndpointResolverBase = (string)e8;
    }
}
