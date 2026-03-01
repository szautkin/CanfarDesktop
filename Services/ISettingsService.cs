namespace CanfarDesktop.Services;

public interface ISettingsService
{
    string ApiBaseUrl { get; set; }
    string DefaultSessionType { get; set; }
    int DefaultCores { get; set; }
    int DefaultRam { get; set; }
    string Theme { get; set; }
    void Save();
    void Load();
}
