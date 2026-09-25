using System.Net;
using Xunit;
using CanfarDesktop.Services;
using CanfarDesktop.Tests.Helpers;

namespace CanfarDesktop.Tests.Services;

public class VizierServiceTests
{
    private static VizierService Service(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        => new(new HttpClient(new MockHttpMessageHandler(handler)));

    private static HttpResponseMessage Csv(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    // ── ADQL construction ─────────────────────────────────────────────────────

    [Fact]
    public void BuildAdql_MatchesTheCanonicalMacOSPattern()
    {
        var adql = VizierService.BuildAdql("V/97/catalog", 298.4438, 18.7792, 0.05, "RAJ2000", "DEJ2000", 500);
        Assert.Equal(
            "SELECT TOP 500 *\n" +
            "FROM \"V/97/catalog\"\n" +
            "WHERE 1 = CONTAINS(\n" +
            "    POINT('ICRS', RAJ2000, DEJ2000),\n" +
            "    CIRCLE('ICRS', 298.4438, 18.7792, 0.05)\n" +
            ")",
            adql);
    }

    [Fact]
    public void BuildAdql_UsesInvariantNumberFormatting_AndCustomColumns()
    {
        var adql = VizierService.BuildAdql("B/vsx/vsx", 10.5, -41.25, 0.001, "RA_ICRS", "DE_ICRS", 42);
        Assert.Contains("SELECT TOP 42 *", adql);
        Assert.Contains("POINT('ICRS', RA_ICRS, DE_ICRS)", adql);
        Assert.Contains("CIRCLE('ICRS', 10.5, -41.25, 0.001)", adql);
    }

    // ── Mirror registry ───────────────────────────────────────────────────────

    /// <summary>
    /// Two of the four mirrors we shipped do not resolve — tap.cds.unistra.fr and
    /// tapvizier.esac.esa.int, which were the FIRST and THIRD entries, so every cone search opened by
    /// spending the per-host budget on two hosts that could not answer. The alias that did work is a
    /// CNAME to tapvizier.cds.unistra.fr, which is the real host and now leads.
    /// </summary>
    [Fact]
    public void DefaultEndpoints_AreTheMirrorsThatResolve_InFailoverOrder()
    {
        Assert.Equal(new[]
        {
            "https://tapvizier.cds.unistra.fr/TAPVizieR/tap/sync",
            "https://tapvizier.u-strasbg.fr/TAPVizieR/tap/sync",
            "http://vizier.china-vo.org/tap/sync",
        }, VizierService.DefaultEndpoints.Select(e => e.SyncUrl).ToArray());

        Assert.DoesNotContain(VizierService.DefaultEndpoints, e => e.Host == "tap.cds.unistra.fr");
        Assert.DoesNotContain(VizierService.DefaultEndpoints, e => e.Host == "tapvizier.esac.esa.int");
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConeSearch_PostsSyncTapForm_AndParsesCsv()
    {
        string? url = null, form = null;
        var svc = Service(async req =>
        {
            url = req.RequestUri!.ToString();
            form = await req.Content!.ReadAsStringAsync();
            return Csv("Name,RAJ2000\nV1,298.44\nV2,298.45\n");
        });

        var (headers, rows) = await svc.ConeSearchAsync("V/97/catalog", 298.4438, 18.7792, 0.05);

        Assert.Equal("https://tapvizier.cds.unistra.fr/TAPVizieR/tap/sync", url);
        Assert.Contains("LANG=ADQL", form);
        Assert.Contains("FORMAT=csv", form);
        Assert.Contains("MAXREC=500", form);
        Assert.Contains("QUERY=", form);
        Assert.Equal(new[] { "Name", "RAJ2000" }, headers);
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "V2", "298.45" }, rows[1]);
    }

    // ── Failover discipline ───────────────────────────────────────────────────

    [Fact]
    public async Task ServerError_RotatesToTheNextMirror()
    {
        var seen = new List<string>();
        var svc = Service(req =>
        {
            seen.Add(req.RequestUri!.Host);
            return Task.FromResult(seen.Count == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("down") }
                : Csv("a\n1\n"));
        });

        var (_, rows) = await svc.ConeSearchAsync("V/97/catalog", 1, 2, 0.01);
        Assert.Equal(new[] { "tapvizier.cds.unistra.fr", "tapvizier.u-strasbg.fr" }, seen);
        Assert.Single(rows);
    }

    [Fact]
    public async Task TransportError_RotatesToTheNextMirror()
    {
        var calls = 0;
        var svc = Service(_ => ++calls == 1
            ? throw new HttpRequestException("No such host is known.")
            : Task.FromResult(Csv("a\n1\n")));

        var (_, rows) = await svc.ConeSearchAsync("V/97/catalog", 1, 2, 0.01);
        Assert.Equal(2, calls);
        Assert.Single(rows);
    }

    [Fact]
    public async Task ClientError_FailsImmediately_WithoutTryingOtherMirrors()
    {
        var calls = 0;
        var svc = Service(_ =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("unknown catalogue"),
            });
        });

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => svc.ConeSearchAsync("nope/nope", 1, 2, 0.01));

        Assert.Equal(1, calls); // 4xx would give the same answer on every mirror
        Assert.Contains("vizier_cone_search at tapvizier.cds.unistra.fr", ex.Message);
        Assert.Contains("not retrying other mirrors (looks like a query problem, not a host problem).", ex.Message);
    }

    [Fact]
    public async Task AllMirrorsDown_ThrowsExhaustedError_NamingEveryHostTried()
    {
        var svc = Service(_ => throw new HttpRequestException("connection refused"));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => svc.ConeSearchAsync("V/97/catalog", 1, 2, 0.01));

        Assert.Contains("vizier_cone_search exhausted all VizieR mirrors " +
            "[tapvizier.cds.unistra.fr, tapvizier.u-strasbg.fr, vizier.china-vo.org]", ex.Message);
        Assert.Contains("last error: connection refused", ex.Message);
        Assert.Contains("use astroquery from inside a Skaha session as a workaround", ex.Message);
    }

    /// <summary>
    /// Only 400 and 403 are definitive. A 404 is the status that LEAST indicates a query problem — it
    /// means the TAP path is not on that host, which is exactly what another mirror might fix — and
    /// stopping the chain on it ended a search at the first mirror that had moved its endpoint.
    /// </summary>
    [Fact]
    public void IsHostFailoverWorthy_OnlyBadRequestAndForbiddenAreDefinitive()
    {
        Assert.True(VizierService.IsHostFailoverWorthy(new HttpRequestException("dns"))); // no status = transport
        Assert.True(VizierService.IsHostFailoverWorthy(
            new HttpRequestException("500", null, HttpStatusCode.InternalServerError)));
        Assert.True(VizierService.IsHostFailoverWorthy(new IOException("reset")));
        Assert.True(VizierService.IsHostFailoverWorthy(new TaskCanceledException())); // per-host timeout

        // The one this changes.
        Assert.True(VizierService.IsHostFailoverWorthy(
            new HttpRequestException("404", null, HttpStatusCode.NotFound)));
        Assert.True(VizierService.IsHostFailoverWorthy(
            new HttpRequestException("503", null, HttpStatusCode.ServiceUnavailable)));

        Assert.False(VizierService.IsHostFailoverWorthy(
            new HttpRequestException("400", null, HttpStatusCode.BadRequest)));
        Assert.False(VizierService.IsHostFailoverWorthy(
            new HttpRequestException("403", null, HttpStatusCode.Forbidden)));
        Assert.False(VizierService.IsHostFailoverWorthy(new InvalidOperationException("parse")));
    }

    // ── The mirror list is a setting ──────────────────────────────────────────

    /// <summary>One URL per line; blanks and # comments ignored; the host comes from the URL.</summary>
    [Fact]
    public void ParseEndpointList_ReadsUrlsAndTakesTheHostFromEach()
    {
        var parsed = VizierService.ParseEndpointList(
            "# my mirrors\nhttps://tap.example.org/tap/sync\n\n  http://other.example/tap/sync  \n");

        Assert.Equal(new[] { "tap.example.org", "other.example" }, parsed.Select(e => e.Host).ToArray());
        Assert.Equal("https://tap.example.org/tap/sync", parsed[0].SyncUrl);
    }

    /// <summary>A typo in the fourth mirror must not take out the first three.</summary>
    [Fact]
    public void ParseEndpointList_DropsUnusableLinesRatherThanTheList()
    {
        var parsed = VizierService.ParseEndpointList(
            "https://good.example/tap/sync\nnot a url\nftp://wrong.example/tap\n");

        Assert.Single(parsed);
        Assert.Equal("good.example", parsed[0].Host);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n # only a comment \n")]
    public void ParseEndpointList_EmptyMeansUseTheDefault(string? text)
        => Assert.Empty(VizierService.ParseEndpointList(text));

    /// <summary>An empty or unset setting falls back to the shipped list rather than to no mirrors.</summary>
    [Fact]
    public void Endpoints_FallBackToTheDefaultWhenNothingIsConfigured()
    {
        var svc = new VizierService(new HttpClient(), () => []);
        Assert.Equal(VizierService.DefaultEndpoints, svc.Endpoints);
    }

    [Fact]
    public void Endpoints_UseTheConfiguredListWhenThereIsOne()
    {
        var mine = VizierService.ParseEndpointList("https://mine.example/tap/sync");
        var svc = new VizierService(new HttpClient(), () => mine);

        Assert.Equal(new[] { "mine.example" }, svc.Endpoints.Select(e => e.Host).ToArray());
    }

    // ── Projection ────────────────────────────────────────────────────────────

    /// <summary>No columns means SELECT *, byte-identical to what the tool has always sent.</summary>
    [Fact]
    public void BuildAdql_WithoutColumns_SelectsEverything()
        => Assert.Contains("SELECT TOP 500 *",
            VizierService.BuildAdql("I/355/gaiadr3", 1, 2, 0.01, "RAJ2000", "DEJ2000", 500));

    /// <summary>
    /// Named columns are quoted, because VizieR own names carry - and _ and mixed case: e_RAJ2000,
    /// Gmag. A Gaia DR3 cone is ~230 columns a row and 500 rows of that is past what a caller can hold.
    /// </summary>
    [Fact]
    public void BuildAdql_WithColumns_QuotesEachName()
    {
        var adql = VizierService.BuildAdql("I/355/gaiadr3", 1, 2, 0.01, "RAJ2000", "DEJ2000", 500,
            ["RAJ2000", "DEJ2000", "Gmag", "e_RAJ2000"]);

        Assert.Contains("SELECT TOP 500 \"RAJ2000\", \"DEJ2000\", \"Gmag\", \"e_RAJ2000\"", adql);
    }

    /// <summary>A name carrying a quote is a caller error, not a value — dropped rather than escaped.</summary>
    [Fact]
    public void BuildAdql_DropsNamesCarryingAQuote()
    {
        var adql = VizierService.BuildAdql("x", 1, 2, 0.01, "RAJ2000", "DEJ2000", 10, ["Gmag", "bad\"name"]);

        Assert.Contains("SELECT TOP 10 \"Gmag\"", adql);
        Assert.DoesNotContain("badname", adql);
    }

    /// <summary>All names unusable falls back to * rather than to a query that selects nothing.</summary>
    [Fact]
    public void BuildAdql_AllNamesUnusable_FallsBackToEverything()
        => Assert.Contains("SELECT TOP 10 *",
            VizierService.BuildAdql("x", 1, 2, 0.01, "RAJ2000", "DEJ2000", 10, ["   ", "\"\""]));

    // ── CSV parsing (same rules as TAPService) ────────────────────────────────

    [Fact]
    public void ParseCsv_QuotedFieldsEscapedQuotesAndMismatchedRows()
    {
        var (headers, rows) = VizierService.ParseCsv(
            "name,note\r\nV1,\"a, b\"\r\nbad-row\r\nV2,\"says \"\"hi\"\"\"\r\n");
        Assert.Equal(new[] { "name", "note" }, headers);
        Assert.Equal(2, rows.Count); // the mismatched row is skipped
        Assert.Equal("a, b", rows[0][1]);
        Assert.Equal("says \"hi\"", rows[1][1]);
    }

    [Fact]
    public void ParseCsv_EmptyInput_ReturnsEmpty()
    {
        var (headers, rows) = VizierService.ParseCsv("");
        Assert.Empty(headers);
        Assert.Empty(rows);
    }
}
