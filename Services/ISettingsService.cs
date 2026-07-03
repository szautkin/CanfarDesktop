namespace CanfarDesktop.Services;

public interface ISettingsService
{
    string ApiBaseUrl { get; set; }

    // ── CANFAR service endpoints (defaults = the standard public hosts; user-editable in Settings).
    // Applied to the ApiEndpoints singleton at startup and on save — URLs are built per request, so
    // changes take effect immediately for new calls.
    string EndpointLoginBase { get; set; }
    string EndpointSkahaBase { get; set; }
    string EndpointAcBase { get; set; }
    string EndpointArcNodes { get; set; }
    string EndpointArcFiles { get; set; }
    string EndpointTapBase { get; set; }
    string EndpointCaom2OpsBase { get; set; }
    string EndpointResolverBase { get; set; }
    /// <summary>Restore every endpoint to its standard CANFAR default.</summary>
    void ResetEndpoints();
    /// <summary>Push the endpoint settings into the live URL builder (validating each value).</summary>
    void ApplyEndpointsTo(Helpers.ApiEndpoints endpoints);

    string DefaultSessionType { get; set; }
    /// <summary>Portal resource preset: "none" | "flexible" | "fixed" (macOS Portal settings parity).</summary>
    string DefaultResourceType { get; set; }
    int DefaultCores { get; set; }
    int DefaultRam { get; set; }
    int DefaultGpus { get; set; }
    string Theme { get; set; }
    /// <summary>App display language: "system" | "en" | "fr". Applied at next launch (macOS parity).</summary>
    string Language { get; set; }
    void Save();
    void Load();
}
