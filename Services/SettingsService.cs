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

    // Endpoint defaults mirror Helpers.ApiEndpoints — one place each; the settings only exist to
    // let a user redirect to a mirror/staging host and must reset to exactly these values.
    internal static class EndpointDefaults
    {
        public const string LoginBase = "https://ws-cadc.canfar.net/ac";
        public const string SkahaBase = "https://ws-uv.canfar.net/skaha";
        public const string AcBase = "https://ws-uv.canfar.net/ac";
        public const string ArcNodes = "https://ws-uv.canfar.net/arc/nodes";
        public const string ArcFiles = "https://ws-uv.canfar.net/arc/files";
        public const string TapBase = "https://ws.cadc-ccda.hia-iha.nrc-cnrc.gc.ca/argus";
        public const string Caom2OpsBase = "https://ws.cadc-ccda.hia-iha.nrc-cnrc.gc.ca/caom2ops";
        public const string ResolverBase = "https://ws.cadc-ccda.hia-iha.nrc-cnrc.gc.ca/cadc-target-resolver";
    }

    public string EndpointLoginBase { get; set; } = EndpointDefaults.LoginBase;
    public string EndpointSkahaBase { get; set; } = EndpointDefaults.SkahaBase;
    public string EndpointAcBase { get; set; } = EndpointDefaults.AcBase;
    public string EndpointArcNodes { get; set; } = EndpointDefaults.ArcNodes;
    public string EndpointArcFiles { get; set; } = EndpointDefaults.ArcFiles;
    public string EndpointTapBase { get; set; } = EndpointDefaults.TapBase;
    public string EndpointCaom2OpsBase { get; set; } = EndpointDefaults.Caom2OpsBase;
    public string EndpointResolverBase { get; set; } = EndpointDefaults.ResolverBase;

    public void ResetEndpoints()
    {
        EndpointLoginBase = EndpointDefaults.LoginBase;
        EndpointSkahaBase = EndpointDefaults.SkahaBase;
        EndpointAcBase = EndpointDefaults.AcBase;
        EndpointArcNodes = EndpointDefaults.ArcNodes;
        EndpointArcFiles = EndpointDefaults.ArcFiles;
        EndpointTapBase = EndpointDefaults.TapBase;
        EndpointCaom2OpsBase = EndpointDefaults.Caom2OpsBase;
        EndpointResolverBase = EndpointDefaults.ResolverBase;
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

        endpoints.LoginBaseUrl = Clean(EndpointLoginBase, EndpointDefaults.LoginBase);
        endpoints.SkahaBaseUrl = Clean(EndpointSkahaBase, EndpointDefaults.SkahaBase);
        endpoints.AcBaseUrl = Clean(EndpointAcBase, EndpointDefaults.AcBase);
        endpoints.ArcNodesRoot = Clean(EndpointArcNodes, EndpointDefaults.ArcNodes);
        endpoints.ArcFilesRoot = Clean(EndpointArcFiles, EndpointDefaults.ArcFiles);
        endpoints.TapBaseUrl = Clean(EndpointTapBase, EndpointDefaults.TapBase);
        endpoints.Caom2OpsBaseUrl = Clean(EndpointCaom2OpsBase, EndpointDefaults.Caom2OpsBase);
        endpoints.ResolverBaseUrl = Clean(EndpointResolverBase, EndpointDefaults.ResolverBase);
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
        _localSettings.Values["ApiBaseUrl"] = ApiBaseUrl;
        _localSettings.Values["DefaultSessionType"] = DefaultSessionType;
        _localSettings.Values["DefaultResourceType"] = DefaultResourceType;
        _localSettings.Values["DefaultCores"] = DefaultCores;
        _localSettings.Values["DefaultRam"] = DefaultRam;
        _localSettings.Values["DefaultGpus"] = DefaultGpus;
        _localSettings.Values["Theme"] = Theme;
        _localSettings.Values["Language"] = Language;
        _localSettings.Values["EndpointLoginBase"] = EndpointLoginBase;
        _localSettings.Values["EndpointSkahaBase"] = EndpointSkahaBase;
        _localSettings.Values["EndpointAcBase"] = EndpointAcBase;
        _localSettings.Values["EndpointArcNodes"] = EndpointArcNodes;
        _localSettings.Values["EndpointArcFiles"] = EndpointArcFiles;
        _localSettings.Values["EndpointTapBase"] = EndpointTapBase;
        _localSettings.Values["EndpointCaom2OpsBase"] = EndpointCaom2OpsBase;
        _localSettings.Values["EndpointResolverBase"] = EndpointResolverBase;
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
