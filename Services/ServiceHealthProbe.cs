using System.Diagnostics;
using System.Net.Http;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Services;

/// <summary>
/// Connectivity self-test over the configured CANFAR/CADC endpoints — shared by the MCP
/// <c>get_service_health</c> tool and the Settings ▸ Service endpoints "Test connections" button.
///
/// <para>It used to send a bare <c>GET</c> to the WORKING endpoints and call any reply healthy. That
/// is not a health question and the services answered accordingly: a bare GET on a TAP sync endpoint
/// is a malformed request that a healthy service answers 400, and <c>whoami</c> answers 401 to an
/// anonymous caller. Three services reported a 4xx beside a tick, and the label was not the fault —
/// the probe was.</para>
///
/// <para>Every IVOA service publishes <c>/availability</c> for exactly this question, and it answers
/// the one thing a status line cannot: a service that is up but announcing planned downtime says so
/// in its own words. Probes run in parallel with a hard 5s cap each, so a dead host can never block
/// the app; failures return an entry, never throw.</para>
///
/// <para>Everything here that does not need an HTTP client lives in <see cref="ServiceAvailability"/>.</para>
/// </summary>
public static class ServiceHealthProbe
{
    /// <summary>One service to probe: where it lives, and whether using it needs a sign-in.</summary>
    private sealed record Target(string Name, string Url, bool RequiresAuth);

    /// <summary>The four core services (the MCP tool's macOS-parity set).</summary>
    public static Task<ServiceProbeResult[]> ProbeCoreAsync(IHttpClientFactory factory, ApiEndpoints e)
        => ProbeAsync(factory,
            new Target("CADC TAP (search)", e.TapBaseUrl, RequiresAuth: false),
            new Target("Skaha (sessions)", e.SkahaBaseUrl, RequiresAuth: true),
            new Target("ARC/VOSpace (storage)", e.StorageBaseUrl, RequiresAuth: true),
            new Target("CADC auth", e.LoginBaseUrl, RequiresAuth: false));

    /// <summary>Every configurable endpoint — one row per Settings field.</summary>
    public static Task<ServiceProbeResult[]> ProbeAllAsync(IHttpClientFactory factory, ApiEndpoints e)
        => ProbeAsync(factory,
            new Target("CADC login (ac)", e.LoginBaseUrl, RequiresAuth: false),
            new Target("Skaha sessions", e.SkahaBaseUrl, RequiresAuth: true),
            new Target("User info (ac)", e.AcBaseUrl, RequiresAuth: true),
            new Target("ARC nodes", e.ArcNodesRoot, RequiresAuth: true),
            new Target("ARC files", e.ArcFilesRoot, RequiresAuth: true),
            new Target("TAP (archive search)", e.TapBaseUrl, RequiresAuth: false),
            new Target("CAOM2 ops", e.Caom2OpsBaseUrl, RequiresAuth: false),
            new Target("Target resolver", e.ResolverBaseUrl, RequiresAuth: false));

    /// <summary>Counts over a set of results: healthy, and of those, usable without signing in.</summary>
    public static ServiceHealthSummary Summarize(IReadOnlyList<ServiceProbeResult> results)
        => ServiceAvailability.Summarize(results);

    private static Task<ServiceProbeResult[]> ProbeAsync(IHttpClientFactory factory, params Target[] targets)
        => Task.WhenAll(targets.Select(t => ProbeOneAsync(factory, t)));

    private static async Task<ServiceProbeResult> ProbeOneAsync(IHttpClientFactory factory, Target target)
    {
        var url = ServiceAvailability.UrlFor(target.Url);
        var sw = Stopwatch.StartNew();
        try
        {
            var client = factory.CreateClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseContentRead, cts.Token);

            // The body is a few hundred bytes of XML; a service that answers something else (a 404 from
            // one that publishes no document) leaves availability unknown rather than false.
            string? body = null;
            if (response.IsSuccessStatusCode)
            {
                try { body = await response.Content.ReadAsStringAsync(cts.Token); }
                catch { /* the status still proves the host is up */ }
            }
            sw.Stop();

            var (available, note) = ServiceAvailability.Read(body);
            return new ServiceProbeResult(
                target.Name, url, Reachable: true, (int)response.StatusCode, sw.ElapsedMilliseconds, null,
                available, note, target.RequiresAuth);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ServiceProbeResult(
                target.Name, url, Reachable: false, null, sw.ElapsedMilliseconds, ex.GetType().Name,
                Available: false, Note: null, RequiresAuth: target.RequiresAuth);
        }
    }
}
