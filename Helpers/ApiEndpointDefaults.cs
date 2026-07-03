namespace CanfarDesktop.Helpers;

/// <summary>
/// The standard public CANFAR/CADC hosts — the SINGLE source of truth for endpoint defaults.
/// ApiEndpoints initializes from these; SettingsService resets to and compares against these
/// (values equal to a default are not persisted, so future default changes reach every user
/// who never customized). Verified against the CADC registry (…/reg/resource-caps).
/// </summary>
public static class ApiEndpointDefaults
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
