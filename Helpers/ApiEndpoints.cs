namespace CanfarDesktop.Helpers;

public class ApiEndpoints
{
    // CANFAR mode base URLs (defaults live in ApiEndpointDefaults — the single source of truth)
    public string LoginBaseUrl { get; set; } = ApiEndpointDefaults.LoginBase;
    public string SkahaBaseUrl { get; set; } = ApiEndpointDefaults.SkahaBase;
    public string AcBaseUrl { get; set; } = ApiEndpointDefaults.AcBase;

    // ARC node/file service roots (scope-agnostic). A caller path selects the tree (see ScopeRootedPath):
    // "projects/<group>/…" → the shared group space; anything else → the user's personal "home/" tree.
    public string ArcNodesRoot { get; set; } = ApiEndpointDefaults.ArcNodes;
    public string ArcFilesRoot { get; set; } = ApiEndpointDefaults.ArcFiles;

    // Personal-tree bases (kept for back-compat + the service-health display); derived from the roots.
    public string StorageBaseUrl => $"{ArcNodesRoot}/home";
    public string StorageFilesBaseUrl => $"{ArcFilesRoot}/home";

    /// <summary>
    /// Resolve a caller path to its scope-rooted ARC path. Group spaces are addressed as
    /// "projects/&lt;group&gt;/…" and pass through untouched; every other path is rooted under the
    /// personal "home/" tree — the long-standing default, so existing home callers are byte-identical.
    /// </summary>
    internal static string ScopeRootedPath(string path)
    {
        var p = (path ?? string.Empty).TrimStart('/');
        return p.StartsWith("projects/", StringComparison.OrdinalIgnoreCase)
            || p.Equals("projects", StringComparison.OrdinalIgnoreCase)
            ? p
            : "home/" + p;
    }

    /// <summary>The "vos://cadc.nrc.ca~arc/…" node URI for a caller path, scope-rooted like the node URL.</summary>
    public string VoSpaceNodeUri(string path) => $"vos://cadc.nrc.ca~arc/{ScopeRootedPath(path)}";

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

    // Storage (VOSpace/ARC) — all scope-aware: a "projects/<group>/…" path hits the group tree,
    // anything else the user's home tree (ScopeRootedPath keeps the home form byte-identical).
    public string StorageUrl(string username) => $"{ArcNodesRoot}/{ScopeRootedPath(username)}?limit=0";
    public string StorageNodeUrl(string path) => $"{ArcNodesRoot}/{ScopeRootedPath(path)}";
    public string StorageNodeListUrl(string path, int? limit = null, string? startUri = null)
    {
        var url = $"{ArcNodesRoot}/{ScopeRootedPath(path)}?detail=max";
        if (limit.HasValue) url += $"&limit={limit.Value}";
        if (startUri is not null) url += $"&uri={Uri.EscapeDataString(startUri)}";
        return url;
    }
    public string StorageFilesUrl(string path) => $"{ArcFilesRoot}/{ScopeRootedPath(path)}";

    // CADC TAP / Search (public, no auth)
    public string TapBaseUrl { get; set; } = ApiEndpointDefaults.TapBase;
    public string TapSyncUrl => $"{TapBaseUrl}/sync";
    public string ResolverBaseUrl { get; set; } = ApiEndpointDefaults.ResolverBase;
    public string ResolverUrl => $"{ResolverBaseUrl}/find";

    // CADC DataLink / Download
    public string Caom2OpsBaseUrl { get; set; } = ApiEndpointDefaults.Caom2OpsBase;
    public string DataLinkUrl(string publisherID) => $"{Caom2OpsBaseUrl}/datalink?id={Uri.EscapeDataString(publisherID)}&request=downloads-only";
    public string DownloadUrl(string publisherID) => $"{Caom2OpsBaseUrl}/pkg?ID={Uri.EscapeDataString(publisherID)}";

    // CAOM2 observation metadata (caom:{collection}/{observationID})
    public string Caom2MetaUrl(string observationUri) => $"{Caom2OpsBaseUrl}/meta?ID={Uri.EscapeDataString(observationUri)}";
}
