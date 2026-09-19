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
/// is a malformed request that a healthy service answers 400, <c>whoami</c> answers 401 to an
/// anonymous caller, and probing a bare base URL just 404s. Three services reported a 4xx beside a
/// tick, and the label was not the fault — the probe was (QA F3).</para>
///
/// <para><b>Two questions, asked in order.</b> Every IVOA service publishes <c>/availability</c>, and
/// that document answers the one thing a status line cannot: a service that is up but announcing
/// planned downtime says so in its own words. Not every endpoint here publishes one, though — so when
/// the document is absent the probe falls back to its working endpoint and judges the STATUS, where
/// 404 (not there) and 5xx (failing) are not healthy however cheerfully the host replied.</para>
///
/// <para>Probes run in parallel with a hard cap each, so a dead host can never block the app;
/// failures return an entry, never throw.</para>
/// </summary>
public static class ServiceHealthProbe
{
    private static readonly TimeSpan PerProbeTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// One service to probe: the endpoint that proves it works, and whether using it needs a sign-in.
    ///
    /// <paramref name="Url"/> is a REAL endpoint rather than a bare base URL — <c>/whoami</c> for the
    /// auth service, because a 401 without credentials still proves the service is there while the
    /// base URL only ever 404'd (QA F3). It is both the fallback probe and the root the availability
    /// document is derived from.
    /// </summary>
    private sealed record Target(string Name, string Url, bool RequiresAuth);

    /// <summary>The four core services (the MCP tool's macOS-parity set).</summary>
    public static Task<ServiceProbeResult[]> ProbeCoreAsync(IHttpClientFactory factory, ApiEndpoints e)
        => ProbeAsync(factory,
            new Target("CADC TAP (search)", e.TapBaseUrl, RequiresAuth: false),
            new Target("Skaha (sessions)", e.SkahaBaseUrl, RequiresAuth: true),
            new Target("ARC/VOSpace (storage)", e.StorageBaseUrl, RequiresAuth: true),
            new Target("CADC auth", e.WhoAmIUrl, RequiresAuth: false));

    /// <summary>Every configurable endpoint — one row per Settings field.</summary>
    public static Task<ServiceProbeResult[]> ProbeAllAsync(IHttpClientFactory factory, ApiEndpoints e)
        => ProbeAsync(factory,
            new Target("CADC login (ac)", e.WhoAmIUrl, RequiresAuth: false),
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
        var availabilityUrl = ServiceAvailability.UrlFor(target.Url);
        var sw = Stopwatch.StartNew();

        try
        {
            var client = factory.CreateClient();
            using var cts = new CancellationTokenSource(PerProbeTimeout);

            using var response = await client.GetAsync(
                availabilityUrl, HttpCompletionOption.ResponseContentRead, cts.Token);
            var status = (int)response.StatusCode;

            // The body is a few hundred bytes of XML. Only read it when the service actually answered.
            string? body = null;
            if (response.IsSuccessStatusCode)
            {
                try { body = await response.Content.ReadAsStringAsync(cts.Token); }
                catch { /* the status still proves the host is up */ }
            }

            var (available, note) = ServiceAvailability.Read(body);

            // No document here. Ask the working endpoint instead, rather than reporting a service down
            // for not speaking VOSI.
            if (available is null && ServiceAvailability.LacksAvailabilityDocument(status))
                return await ProbeEndpointAsync(client, target, sw, cts.Token);

            sw.Stop();
            return new ServiceProbeResult(
                target.Name, availabilityUrl, Reachable: true, status, sw.ElapsedMilliseconds, null,
                available, note, target.RequiresAuth, Ok: ServiceAvailability.IsHealthyStatus(status));
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ServiceProbeResult(
                target.Name, availabilityUrl, Reachable: false, null, sw.ElapsedMilliseconds, ex.GetType().Name,
                Available: false, Note: null, RequiresAuth: target.RequiresAuth, Ok: false);
        }
    }

    /// <summary>
    /// The fallback: judge the service by what its working endpoint answers. Availability stays null —
    /// unknown rather than false, because a service that publishes no document has not said anything
    /// about itself either way.
    /// </summary>
    private static async Task<ServiceProbeResult> ProbeEndpointAsync(
        HttpClient client, Target target, Stopwatch sw, CancellationToken ct)
    {
        try
        {
            using var response = await client.GetAsync(target.Url, HttpCompletionOption.ResponseHeadersRead, ct);
            sw.Stop();
            var status = (int)response.StatusCode;

            return new ServiceProbeResult(
                target.Name, target.Url, Reachable: true, status, sw.ElapsedMilliseconds, null,
                Available: null, Note: null, RequiresAuth: target.RequiresAuth,
                Ok: ServiceAvailability.IsHealthyStatus(status));
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ServiceProbeResult(
                target.Name, target.Url, Reachable: false, null, sw.ElapsedMilliseconds, ex.GetType().Name,
                Available: false, Note: null, RequiresAuth: target.RequiresAuth, Ok: false);
        }
    }
}
