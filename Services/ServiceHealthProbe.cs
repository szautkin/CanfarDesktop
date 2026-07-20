using System.Diagnostics;
using System.Net.Http;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Services;

/// <summary>
/// One probed host. <c>Reachable</c> = any HTTP response arrived (the HOST is up; a 401 still proves
/// that). <c>Ok</c> = the endpoint also answered sanely — 404 (endpoint missing / wrong URL) and 5xx
/// (server error) are NOT ok. QA F3 hit exactly this gap: a 404 was reported as a healthy service.
/// </summary>
public sealed record ServiceProbeResult(string Name, string Url, bool Reachable, bool Ok, int? StatusCode, long LatencyMs, string? Error);

/// <summary>
/// Connectivity self-test over the configured CANFAR/CADC endpoints — shared by the MCP
/// <c>get_service_health</c> tool and the Settings ▸ Service endpoints "Test connections" button.
/// Probes run in parallel with a hard 5s cap each, headers-only, so a dead host can never block
/// the app; failures return an entry, never throw.
/// </summary>
public static class ServiceHealthProbe
{
    /// <summary>The four core services (the MCP tool's macOS-parity set). The auth probe targets a
    /// REAL endpoint (/whoami — a 401 without credentials still proves the service works); probing the
    /// bare base URL always 404'd, so it only ever proved the host, not the service (QA F3).</summary>
    public static Task<ServiceProbeResult[]> ProbeCoreAsync(IHttpClientFactory factory, ApiEndpoints e)
        => ProbeAsync(factory,
            ("CADC TAP (search)", e.TapBaseUrl),
            ("Skaha (sessions)", e.SkahaBaseUrl),
            ("ARC/VOSpace (storage)", e.StorageBaseUrl),
            ("CADC auth", e.WhoAmIUrl));

    /// <summary>Every configurable endpoint — one row per Settings field.</summary>
    public static Task<ServiceProbeResult[]> ProbeAllAsync(IHttpClientFactory factory, ApiEndpoints e)
        => ProbeAsync(factory,
            ("CADC login (ac)", e.WhoAmIUrl),
            ("Skaha sessions", e.SkahaBaseUrl),
            ("User info (ac)", e.AcBaseUrl),
            ("ARC nodes", e.ArcNodesRoot),
            ("ARC files", e.ArcFilesRoot),
            ("TAP (archive search)", e.TapBaseUrl),
            ("CAOM2 ops", e.Caom2OpsBaseUrl),
            ("Target resolver", e.ResolverBaseUrl));

    private static Task<ServiceProbeResult[]> ProbeAsync(IHttpClientFactory factory, params (string Name, string Url)[] targets)
        => Task.WhenAll(targets.Select(t => ProbeOneAsync(factory, t.Name, t.Url)));

    private static async Task<ServiceProbeResult> ProbeOneAsync(IHttpClientFactory factory, string name, string url)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var client = factory.CreateClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            sw.Stop();
            int status = (int)response.StatusCode;
            return new ServiceProbeResult(name, url, Reachable: true, Ok: IsHealthyStatus(status), status, sw.ElapsedMilliseconds, null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ServiceProbeResult(name, url, Reachable: false, Ok: false, null, sw.ElapsedMilliseconds, ex.GetType().Name);
        }
    }

    /// <summary>2xx/3xx and auth-gated answers (401/403/405…) prove a live service; 404 means the
    /// endpoint doesn't exist and 5xx means it's failing — neither may be reported healthy.</summary>
    private static bool IsHealthyStatus(int status) => status != 404 && status < 500;
}
