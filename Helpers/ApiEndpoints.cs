namespace CanfarDesktop.Helpers;

public class ApiEndpoints
{
    // CANFAR mode base URLs
    public string LoginBaseUrl { get; set; } = "https://ws-cadc.canfar.net/ac";
    public string SkahaBaseUrl { get; set; } = "https://ws-uv.canfar.net/skaha";
    public string AcBaseUrl { get; set; } = "https://ws-uv.canfar.net/ac";
    public string StorageBaseUrl { get; set; } = "https://ws-uv.canfar.net/arc/nodes/home";

    // Auth
    public string LoginUrl => $"{LoginBaseUrl}/login";
    public string WhoAmIUrl => $"{LoginBaseUrl}/whoami";
    public string UserUrl(string username) => $"{AcBaseUrl}/users/{username}";

    // Skaha Sessions
    public string SessionsUrl => $"{SkahaBaseUrl}/v1/session";
    public string SessionUrl(string id) => $"{SkahaBaseUrl}/v1/session/{id}";
    public string SessionRenewUrl(string id) => $"{SkahaBaseUrl}/v1/session/{id}/renew";
    public string SessionEventsUrl(string id) => $"{SkahaBaseUrl}/v1/session/{id}?view=events";
    public string SessionLogsUrl(string id) => $"{SkahaBaseUrl}/v1/session/{id}?view=logs";

    // Skaha Images/Context/Stats
    public string ImagesUrl => $"{SkahaBaseUrl}/v1/image";
    public string ContextUrl => $"{SkahaBaseUrl}/v1/context";
    public string StatsUrl => $"{SkahaBaseUrl}/v1/session?view=stats";
    public string RepositoryUrl => $"{SkahaBaseUrl}/v1/repository";

    // Storage (VOSpace/ARC)
    public string StorageUrl(string username) => $"{StorageBaseUrl}/{username}";
}
