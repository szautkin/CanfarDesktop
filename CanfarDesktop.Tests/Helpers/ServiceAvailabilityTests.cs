using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// The IVOA availability question, without a network.
///
/// <para>The distinction this whole file exists to protect is "down" versus "we do not know". A
/// service that publishes no availability document is unknown, and reporting unknown as down tells
/// somebody their archive is broken when it is answering perfectly.</para>
/// </summary>
public class ServiceAvailabilityTests
{
    // ── Where the document lives ────────────────────────────────────────────────────────────────

    /// <summary>The document is at the SERVICE root, so a configured sub-path is trimmed first.</summary>
    [Theory]
    [InlineData("https://ws.cadc.ca/vault", "https://ws.cadc.ca/vault/availability")]
    [InlineData("https://ws.cadc.ca/vault/nodes", "https://ws.cadc.ca/vault/availability")]
    [InlineData("https://ws.cadc.ca/vault/files", "https://ws.cadc.ca/vault/availability")]
    [InlineData("https://ws.cadc.ca/tap/sync", "https://ws.cadc.ca/tap/availability")]
    [InlineData("https://ws.cadc.ca/tap/async", "https://ws.cadc.ca/tap/availability")]
    [InlineData("https://ws.cadc.ca/arc/home", "https://ws.cadc.ca/arc/availability")]
    public void TheDocumentSitsAtTheServiceRoot(string configured, string expected)
        => Assert.Equal(expected, ServiceAvailability.UrlFor(configured));

    /// <summary>A trailing slash is not a different service.</summary>
    [Fact]
    public void ATrailingSlashChangesNothing()
        => Assert.Equal(
            ServiceAvailability.UrlFor("https://ws.cadc.ca/tap"),
            ServiceAvailability.UrlFor("https://ws.cadc.ca/tap/"));

    /// <summary>
    /// Only a KNOWN sub-path is trimmed. Dropping the last segment of anything would walk out of the
    /// services whose configured base already IS the root — "/caom2" is a service, not a sub-path.
    /// </summary>
    [Fact]
    public void AnUnknownLastSegmentIsNotMistakenForASubPath()
        => Assert.Equal("https://ws.cadc.ca/caom2/availability",
                        ServiceAvailability.UrlFor("https://ws.cadc.ca/caom2"));

    [Fact]
    public void NothingConfiguredDoesNotThrow()
        => Assert.Equal("/availability", ServiceAvailability.UrlFor(""));

    // ── What it says ────────────────────────────────────────────────────────────────────────────

    /// <summary>The prefix varies between services, and a namespace prefix is not information.</summary>
    [Theory]
    [InlineData("<availability><available>true</available></availability>")]
    [InlineData("<vosi:availability><vosi:available>true</vosi:available></vosi:availability>")]
    [InlineData("<ns2:availability><ns2:available> true </ns2:available></ns2:availability>")]
    [InlineData("<AVAILABILITY><AVAILABLE>TRUE</AVAILABLE></AVAILABILITY>")]
    public void AnyPrefixAndAnyCasingReadsAsUp(string body)
        => Assert.True(ServiceAvailability.Read(body).Available);

    [Fact]
    public void AServiceThatDeclaresItselfDownIsDown()
        => Assert.False(ServiceAvailability.Read(
            "<vosi:availability><vosi:available>false</vosi:available></vosi:availability>").Available);

    /// <summary>
    /// The reason travels with the refusal. A service that is down and says why is the one case where
    /// there is something useful to pass on.
    /// </summary>
    [Fact]
    public void ItsReasonForBeingDownIsCarried()
    {
        var (available, note) = ServiceAvailability.Read(
            "<availability><available>false</available><note>Scheduled maintenance</note></availability>");

        Assert.False(available);
        Assert.Equal("Scheduled maintenance", note);
    }

    /// <summary>A note spanning lines is still one note.</summary>
    [Fact]
    public void AMultiLineNoteIsReadWhole()
        => Assert.Equal("down for\n  maintenance",
            ServiceAvailability.Read(
                "<availability><available>false</available><note>down for\n  maintenance</note></availability>").Note);

    /// <summary>
    /// Unreadable is UNKNOWN, not down. This is the property the whole type exists for: a service
    /// that publishes nothing is answering fine, and calling it down is a false alarm about an
    /// archive that works.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<html><body>404 Not Found</body></html>")]
    [InlineData("{\"available\": true}")]
    [InlineData("<availability><available>maybe</available></availability>")]
    public void AnythingUnreadableIsUnknownRatherThanDown(string? body)
    {
        var (available, note) = ServiceAvailability.Read(body);

        Assert.Null(available);
        Assert.Null(note);
    }

    /// <summary>An empty note is no note, not an empty string somebody has to render.</summary>
    [Fact]
    public void AnEmptyNoteIsNoNote()
        => Assert.Null(ServiceAvailability.Read(
            "<availability><available>true</available><note>  </note></availability>").Note);

    // ── What a status line proves ───────────────────────────────────────────────────────────────

    /// <summary>
    /// An auth-gated answer still proves something is there and working. A service you must sign in
    /// to use is not a broken service.
    /// </summary>
    [Theory]
    [InlineData(200)]
    [InlineData(302)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(405)]
    public void AnAnswerThatProvesSomethingIsThereIsHealthy(int status)
        => Assert.True(ServiceAvailability.IsHealthyStatus(status));

    /// <summary>404 is nothing there and 5xx is failing; neither is healthy because the host replied.</summary>
    [Theory]
    [InlineData(404)]
    [InlineData(500)]
    [InlineData(503)]
    public void MissingOrFailingIsNotHealthy(int status)
        => Assert.False(ServiceAvailability.IsHealthyStatus(status));

    /// <summary>The statuses that mean "no document here", so the caller judges the service elsewhere.</summary>
    [Theory]
    [InlineData(404, true)]
    [InlineData(405, true)]
    [InlineData(501, true)]
    [InlineData(200, false)]
    [InlineData(503, false)]
    public void TheNoDocumentStatusesAreRecognised(int status, bool lacks)
        => Assert.Equal(lacks, ServiceAvailability.LacksAvailabilityDocument(status));

    // ── One service's verdict ───────────────────────────────────────────────────────────────────

    private static ServiceProbeResult Probe(
        bool reachable = true, bool ok = true, bool? available = null, bool requiresAuth = false)
        => new("svc", "https://x/availability", reachable, 200, 10, null, available, null, requiresAuth, ok);

    [Fact]
    public void HealthyNeedsTheHostTheEndpointAndTheService()
    {
        Assert.True(Probe().IsHealthy);
        Assert.False(Probe(reachable: false).IsHealthy);      // host said nothing
        Assert.False(Probe(ok: false).IsHealthy);             // endpoint 404 or 5xx
        Assert.False(Probe(available: false).IsHealthy);      // service declares itself down
    }

    /// <summary>
    /// Unknown availability is still healthy. Demanding a document would mark every service that
    /// publishes none as unhealthy, which is most of the reason this distinction exists.
    /// </summary>
    [Fact]
    public void NoDocumentDoesNotMakeAServiceUnhealthy()
        => Assert.True(Probe(available: null).IsHealthy);

    /// <summary>
    /// Signed out, every service can be healthy and none usable. That is why the two are counted
    /// apart rather than one being derived from the other.
    /// </summary>
    [Fact]
    public void AuthGatedIsHealthyButNotUsableAnonymously()
    {
        var gated = Probe(requiresAuth: true);

        Assert.True(gated.IsHealthy);
        Assert.False(gated.IsUsableAnonymously);
    }

    // ── A whole run ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheSummaryCountsHealthyAndUsableSeparately()
    {
        var summary = ServiceAvailability.Summarize([
            Probe(),                        // healthy, usable
            Probe(requiresAuth: true),      // healthy, not usable
            Probe(available: false),        // down
            Probe(reachable: false),        // unreachable
        ]);

        Assert.Equal(4, summary.Count);
        Assert.Equal(2, summary.HealthyCount);
        Assert.Equal(1, summary.UsableCount);
    }

    [Fact]
    public void AnEmptyRunCountsZeroRatherThanThrowing()
    {
        var summary = ServiceAvailability.Summarize([]);

        Assert.Equal(0, summary.Count);
        Assert.Equal(0, summary.HealthyCount);
    }
}
