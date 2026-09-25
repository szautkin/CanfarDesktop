using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Services;

/// <summary>
/// The probe used to ask the wrong question — a bare GET on the working endpoint — and then call any
/// reply healthy, so a TAP service answering 400 to a malformed request and a whoami answering 401 to
/// an anonymous caller both got a tick. These cover the question it asks now.
/// </summary>
public class ServiceHealthProbeTests
{
    // ── Where the document lives ─────────────────────────────────────────────

    /// <summary>
    /// The availability document is at the SERVICE root. Most configured bases already ARE that root;
    /// the ARC ones point a segment inside it, which is why the sub-path is trimmed by name rather
    /// than by dropping the last segment (that would walk out of the others).
    /// </summary>
    [Theory]
    [InlineData("https://ws.cadc-ccda.hia-iha.nrc-cnrc.gc.ca/argus",
                "https://ws.cadc-ccda.hia-iha.nrc-cnrc.gc.ca/argus/availability")]
    [InlineData("https://ws-uv.canfar.net/skaha", "https://ws-uv.canfar.net/skaha/availability")]
    [InlineData("https://ws-cadc.canfar.net/ac", "https://ws-cadc.canfar.net/ac/availability")]
    [InlineData("https://ws-uv.canfar.net/arc/nodes", "https://ws-uv.canfar.net/arc/availability")]
    [InlineData("https://ws-uv.canfar.net/arc/files", "https://ws-uv.canfar.net/arc/availability")]
    [InlineData("https://ws-uv.canfar.net/arc/nodes/home", "https://ws-uv.canfar.net/arc/nodes/availability")]
    [InlineData("https://example.org/tap/sync", "https://example.org/tap/availability")]
    public void AvailabilityUrl_ResolvesToTheServiceRoot(string baseUrl, string expected)
        => Assert.Equal(expected, ServiceAvailability.UrlFor(baseUrl));

    [Theory]
    [InlineData("https://example.org/skaha/", "https://example.org/skaha/availability")]
    [InlineData("", "/availability")]
    public void AvailabilityUrl_ToleratesTrailingSlashesAndEmptyInput(string baseUrl, string expected)
        => Assert.Equal(expected, ServiceAvailability.UrlFor(baseUrl));

    // ── Reading the document ─────────────────────────────────────────────────

    /// <summary>Namespace-agnostic: the prefix varies between services and a prefix is not information.</summary>
    [Theory]
    [InlineData("<vosi:availability><vosi:available>true</vosi:available></vosi:availability>")]
    [InlineData("<availability><available>true</available></availability>")]
    [InlineData("<ns2:availability><ns2:available>TRUE</ns2:available></ns2:availability>")]
    public void ReadAvailability_ReadsTrueWhateverThePrefix(string body)
        => Assert.True(ServiceAvailability.Read(body).Available);

    /// <summary>A service announcing downtime says so, in its own words — which is the useful part.</summary>
    [Fact]
    public void ReadAvailability_CarriesTheServicesOwnNote()
    {
        var (available, note) = ServiceAvailability.Read(
            "<vosi:availability><vosi:available>false</vosi:available>" +
            "<vosi:note>Scheduled maintenance until 14:00 UTC</vosi:note></vosi:availability>");

        Assert.False(available);
        Assert.Equal("Scheduled maintenance until 14:00 UTC", note);
    }

    /// <summary>
    /// A service that publishes no readable document is UNKNOWN, not down. Reporting an outage this
    /// probe cannot see would be inventing one.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<html><body>404 Not Found</body></html>")]
    [InlineData("{\"status\":\"ok\"}")]
    public void ReadAvailability_UnreadableIsUnknownNotDown(string? body)
    {
        var (available, note) = ServiceAvailability.Read(body);
        Assert.Null(available);
        Assert.Null(note);
    }

    // ── Healthy, and usable ──────────────────────────────────────────────────

    private static ServiceProbeResult Result(bool reachable, bool? available, bool requiresAuth)
        => new("svc", "https://x/availability", reachable, 200, 1, null, available, null, requiresAuth);

    /// <summary>Up as far as anyone can tell: reachable, and not declaring itself down.</summary>
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, null, true)]     // publishes no document — unknown is not down
    [InlineData(true, false, false)]   // declares itself unavailable
    [InlineData(false, null, false)]   // host did not answer
    public void IsHealthy_IsReachableAndNotDeclaringItselfDown(bool reachable, bool? available, bool expected)
        => Assert.Equal(expected, Result(reachable, available, requiresAuth: false).IsHealthy);

    /// <summary>
    /// The count that was missing. Signed out, every service can be healthy and none of them usable,
    /// so the two numbers answer different questions and both are reported.
    /// </summary>
    [Fact]
    public void Summarize_CountsHealthyAndUsableSeparately()
    {
        var results = new[]
        {
            Result(reachable: true, available: true, requiresAuth: false),   // healthy + usable
            Result(reachable: true, available: null, requiresAuth: true),    // healthy, needs sign-in
            Result(reachable: true, available: false, requiresAuth: false),  // announcing downtime
            Result(reachable: false, available: false, requiresAuth: true),  // down
        };

        var summary = ServiceAvailability.Summarize(results);

        Assert.Equal(4, summary.Count);
        Assert.Equal(2, summary.HealthyCount);
        Assert.Equal(1, summary.UsableCount);
    }

    /// <summary>An auth-gated service that is up is healthy — it is just not usable while signed out.</summary>
    [Fact]
    public void RequiresAuth_DoesNotMakeAServiceUnhealthy()
    {
        var r = Result(reachable: true, available: true, requiresAuth: true);
        Assert.True(r.IsHealthy);
        Assert.False(r.IsUsableAnonymously);
    }
}
