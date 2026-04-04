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
    public string SessionRenewUrl(string id) => $"{SkahaBaseUrl}/v1/session/{id}?action=renew";
    public string SessionEventsUrl(string id) => $"{SkahaBaseUrl}/v1/session/{id}?view=events";
    public string SessionLogsUrl(string id) => $"{SkahaBaseUrl}/v1/session/{id}?view=logs";

    // Skaha Images/Context/Stats
    public string ImagesUrl => $"{SkahaBaseUrl}/v1/image";
    public string ContextUrl => $"{SkahaBaseUrl}/v1/context";
    public string StatsUrl => $"{SkahaBaseUrl}/v1/session?view=stats";
    public string RepositoryUrl => $"{SkahaBaseUrl}/v1/repository";

    // Storage (VOSpace/ARC)
    public string StorageUrl(string username) => $"{StorageBaseUrl}/{username}?limit=0";
    public string StorageNodeUrl(string path) => $"{StorageBaseUrl}/{path}";
    public string StorageNodeListUrl(string path, int? limit = null, string? startUri = null)
    {
        var url = $"{StorageBaseUrl}/{path}?detail=max";
        if (limit.HasValue) url += $"&limit={limit.Value}";
        if (startUri is not null) url += $"&uri={Uri.EscapeDataString(startUri)}";
        return url;
    }
    public string StorageFilesBaseUrl { get; set; } = "https://ws-uv.canfar.net/arc/files/home";
    public string StorageFilesUrl(string path) => $"{StorageFilesBaseUrl}/{path}";

    // CADC TAP / Search (public, no auth)
    public string TapBaseUrl { get; set; } = "https://ws.cadc-ccda.hia-iha.nrc-cnrc.gc.ca/argus";
    public string TapSyncUrl => $"{TapBaseUrl}/sync";
    public string ResolverUrl => "https://ws.cadc-ccda.hia-iha.nrc-cnrc.gc.ca/cadc-target-resolver/find";

    // CADC DataLink / Download
    public string Caom2OpsBaseUrl { get; set; } = "https://ws.cadc-ccda.hia-iha.nrc-cnrc.gc.ca/caom2ops";
    public string DataLinkUrl(string publisherID) => $"{Caom2OpsBaseUrl}/datalink?id={Uri.EscapeDataString(publisherID)}&request=downloads-only";
    public string DownloadUrl(string publisherID) => $"{Caom2OpsBaseUrl}/pkg?ID={Uri.EscapeDataString(publisherID)}";
}
