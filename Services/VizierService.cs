using System.Globalization;
using System.Net.Http;

namespace CanfarDesktop.Services;

/// <summary>One public VizieR TAP mirror — host + canonical <c>/sync</c> URL. <c>Host</c> is exposed
/// separately so error messages can surface the rotation path without parsing URLs back out.</summary>
public sealed record VizierEndpoint(string Host, string SyncUrl);

/// <summary>
/// VizieR cone search with mirror failover. Builds the canonical ADQL pattern
/// (<c>CIRCLE</c>+<c>CONTAINS</c> against the catalogue's RA/Dec columns) and rotates through the
/// public VizieR TAP mirrors when a host is unreachable. Only host-specific errors (transport
/// failures, timeouts, 5xx) trigger rotation — a 4xx (bad ADQL, unknown catalogue) would give the
/// same answer on every mirror, so it raises immediately. Public service, no auth. Ports the macOS
/// TAPClient.vizierConeSearch, including its mirror registry and failover discipline.
/// </summary>
public class VizierService
{
    /// <summary>Per-host budget before the failover rotates to the next mirror (see the 90s tool
    /// deadline: enough for two-host fallback without false-failing a slow-but-working primary).</summary>
    internal static readonly TimeSpan PerHostTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Ordered fallback list of VizieR TAP mirrors: primary CDS, CDS's legacy alias (different DNS
    /// zone), ESAC (geographically distinct, separate operator), then the China-VO HTTP mirror
    /// (last resort for when TLS itself is broken). All four mirror the same catalogue corpus.
    /// </summary>
    public static readonly IReadOnlyList<VizierEndpoint> Endpoints = new[]
    {
        new VizierEndpoint("tap.cds.unistra.fr", "https://tap.cds.unistra.fr/tap/sync"),
        new VizierEndpoint("tapvizier.u-strasbg.fr", "https://tapvizier.u-strasbg.fr/TAPVizieR/tap/sync"),
        new VizierEndpoint("tapvizier.esac.esa.int", "https://tapvizier.esac.esa.int/TAPVizieR/tap/sync"),
        new VizierEndpoint("vizier.china-vo.org", "http://vizier.china-vo.org/tap/sync"),
    };

    private readonly HttpClient _httpClient;

    public VizierService(HttpClient httpClient) => _httpClient = httpClient;

    /// <summary>The canonical VizieR cone-search ADQL (byte-compatible with the macOS TAPClient).</summary>
    public static string BuildAdql(
        string catalogue, double raDeg, double decDeg, double radiusDeg,
        string raColumn, string decColumn, int maxRec)
    {
        var inv = CultureInfo.InvariantCulture;
        return $"SELECT TOP {maxRec.ToString(inv)} *\n" +
               $"FROM \"{catalogue}\"\n" +
               "WHERE 1 = CONTAINS(\n" +
               $"    POINT('ICRS', {raColumn}, {decColumn}),\n" +
               $"    CIRCLE('ICRS', {raDeg.ToString(inv)}, {decDeg.ToString(inv)}, {radiusDeg.ToString(inv)})\n" +
               ")";
    }

    public virtual async Task<(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows)> ConeSearchAsync(
        string catalogue, double raDeg, double decDeg, double radiusDeg,
        string raColumn = "RAJ2000", string decColumn = "DEJ2000", int maxRec = 500,
        CancellationToken cancellationToken = default)
    {
        var adql = BuildAdql(catalogue, raDeg, decDeg, radiusDeg, raColumn, decColumn, maxRec);

        var attempts = new List<(string Host, string Error)>();
        foreach (var endpoint in Endpoints)
        {
            string csv;
            try
            {
                csv = await QueryOnceAsync(endpoint.SyncUrl, adql, maxRec, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                if (!IsHostFailoverWorthy(ex))
                {
                    // 4xx / parse / catalogue-not-found: failing on this mirror means failing on all
                    // of them. Don't waste budget trying further hosts.
                    throw new HttpRequestException(
                        $"vizier_cone_search at {endpoint.Host}: {ex.Message} — not retrying other mirrors (looks like a query problem, not a host problem).");
                }
                attempts.Add((endpoint.Host, ex.Message));
                continue;
            }
            return ParseCsv(csv);
        }

        var tried = string.Join(", ", attempts.Select(a => a.Host));
        var lastReason = attempts.Count > 0 ? attempts[^1].Error : "unknown";
        throw new HttpRequestException(
            $"vizier_cone_search exhausted all VizieR mirrors [{tried}]; last error: {lastReason}. " +
            "VizieR may be globally degraded — retry in a few minutes, or use astroquery from inside a Skaha session as a workaround.");
    }

    /// <summary>
    /// Predicate for "this error means THIS HOST is the problem, try the next one": any transport
    /// failure (DNS, TLS, connection refused) or per-host timeout, and any 5xx. A 4xx is NOT — the
    /// request is wrong and every mirror will tell us the same thing.
    /// </summary>
    internal static bool IsHostFailoverWorthy(Exception ex) => ex switch
    {
        HttpRequestException h => h.StatusCode is null || (int)h.StatusCode >= 500,
        OperationCanceledException => true, // per-host timeout (caller cancellation never reaches here)
        IOException => true,
        _ => false,
    };

    /// <summary>One sync TAP POST against one mirror (same form encoding as <see cref="TAPService"/>).</summary>
    private async Task<string> QueryOnceAsync(string url, string adql, int maxRec, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("LANG", "ADQL"),
            new KeyValuePair<string, string>("FORMAT", "csv"),
            new KeyValuePair<string, string>("MAXREC", maxRec.ToString(CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("QUERY", adql),
        });

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(PerHostTimeout);

        using var response = await _httpClient.PostAsync(url, content, cts.Token);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cts.Token);
            throw new HttpRequestException(
                $"VizieR query failed ({(int)response.StatusCode}): {Clip(body)}", null, response.StatusCode);
        }
        return await response.Content.ReadAsStringAsync(cts.Token);
    }

    private static string Clip(string s) => s.Length <= 300 ? s : s[..300];

    /// <summary>Parse a TAP CSV response into headers + rows (same rules as <see cref="TAPService"/>:
    /// quoted fields, escaped quotes, rows with a mismatched column count are skipped).</summary>
    internal static (IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows) ParseCsv(string csv)
    {
        var lines = csv.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
            return (Array.Empty<string>(), Array.Empty<IReadOnlyList<string>>());

        var headers = ParseCsvLine(lines[0]);
        var rows = new List<IReadOnlyList<string>>();
        for (var i = 1; i < lines.Length; i++)
        {
            var values = ParseCsvLine(lines[i]);
            if (values.Count == headers.Count) rows.Add(values);
        }
        return (headers, rows);
    }

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var inQuotes = false;
        var field = new System.Text.StringBuilder();

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(field.ToString().Trim());
                field.Clear();
            }
            else
            {
                field.Append(c);
            }
        }
        fields.Add(field.ToString().Trim());
        return fields;
    }
}
