namespace CanfarDesktop.Services;

public interface ISettingsService
{
    string ApiBaseUrl { get; set; }
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
