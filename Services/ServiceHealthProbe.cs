using System.Diagnostics;
using System.Net.Http;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Services;

/// <summary>One probed host: any HTTP status counts as reachable (401/404 still prove the host is up).</summary>
public sealed record ServiceProbeResult(string Name, string Url, bool Reachable, int? StatusCode, long LatencyMs, string? Error);

/// <summary>
/// Connectivity self-test over the configured CANFAR/CADC endpoints — shared by the MCP
/// <c>get_service_health</c> tool and the Settings ▸ Service endpoints "Test connections" button.
/// Probes run in parallel with a hard 5s cap each, headers-only, so a dead host can never block
/// the app; failures return an entry, never throw.
/// </summary>
public static class ServiceHealthProbe
{
    /// <summary>The four core services (the MCP tool's macOS-parity set).</summary>
    public static Task<ServiceProbeResult[]> ProbeCoreAsync(IHttpClientFactory factory, ApiEndpoints e)
        => ProbeAsync(factory,
            ("CADC TAP (search)", e.TapBaseUrl),
            ("Skaha (sessions)", e.SkahaBaseUrl),
            ("ARC/VOSpace (storage)", e.StorageBaseUrl),
            ("CADC auth", e.LoginBaseUrl));

    /// <summary>Every configurable endpoint — one row per Settings field.</summary>
    public static Task<ServiceProbeResult[]> ProbeAllAsync(IHttpClientFactory factory, ApiEndpoints e)
        => ProbeAsync(factory,
            ("CADC login (ac)", e.LoginBaseUrl),
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
            return new ServiceProbeResult(name, url, Reachable: true, (int)response.StatusCode, sw.ElapsedMilliseconds, null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ServiceProbeResult(name, url, Reachable: false, null, sw.ElapsedMilliseconds, ex.GetType().Name);
        }
    }
}
