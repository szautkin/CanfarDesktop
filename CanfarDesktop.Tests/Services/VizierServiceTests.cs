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

    [Fact]
    public void Endpoints_AreTheFourMacOSMirrorsInFailoverOrder()
    {
        Assert.Equal(new[]
        {
            "https://tap.cds.unistra.fr/tap/sync",
            "https://tapvizier.u-strasbg.fr/TAPVizieR/tap/sync",
            "https://tapvizier.esac.esa.int/TAPVizieR/tap/sync",
            "http://vizier.china-vo.org/tap/sync",
        }, VizierService.Endpoints.Select(e => e.SyncUrl).ToArray());
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

        Assert.Equal("https://tap.cds.unistra.fr/tap/sync", url);
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
        Assert.Equal(new[] { "tap.cds.unistra.fr", "tapvizier.u-strasbg.fr" }, seen);
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
        Assert.Contains("vizier_cone_search at tap.cds.unistra.fr", ex.Message);
        Assert.Contains("not retrying other mirrors (looks like a query problem, not a host problem).", ex.Message);
    }

    [Fact]
    public async Task AllMirrorsDown_ThrowsExhaustedError_NamingEveryHostTried()
    {
        var svc = Service(_ => throw new HttpRequestException("connection refused"));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => svc.ConeSearchAsync("V/97/catalog", 1, 2, 0.01));

        Assert.Contains("vizier_cone_search exhausted all VizieR mirrors " +
            "[tap.cds.unistra.fr, tapvizier.u-strasbg.fr, tapvizier.esac.esa.int, vizier.china-vo.org]", ex.Message);
        Assert.Contains("last error: connection refused", ex.Message);
        Assert.Contains("use astroquery from inside a Skaha session as a workaround", ex.Message);
    }

    [Fact]
    public void IsHostFailoverWorthy_TransportAnd5xxRotate_4xxDoesNot()
    {
        Assert.True(VizierService.IsHostFailoverWorthy(new HttpRequestException("dns"))); // no status = transport
        Assert.True(VizierService.IsHostFailoverWorthy(
            new HttpRequestException("500", null, HttpStatusCode.InternalServerError)));
        Assert.True(VizierService.IsHostFailoverWorthy(new IOException("reset")));
        Assert.True(VizierService.IsHostFailoverWorthy(new TaskCanceledException())); // per-host timeout
        Assert.False(VizierService.IsHostFailoverWorthy(
            new HttpRequestException("400", null, HttpStatusCode.BadRequest)));
        Assert.False(VizierService.IsHostFailoverWorthy(new InvalidOperationException("parse")));
    }

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
