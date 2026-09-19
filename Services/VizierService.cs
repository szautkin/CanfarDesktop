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
    /// Ordered fallback list of VizieR TAP mirrors: the canonical CDS host, its legacy alias, then the
    /// China-VO HTTP mirror (last resort for when TLS itself is broken). All mirror the same corpus.
    ///
    /// <para>Two entries are gone. <c>tap.cds.unistra.fr</c> and <c>tapvizier.esac.esa.int</c> do not
    /// resolve — checked, not assumed — and they were the FIRST and THIRD entries, so every cone
    /// search opened by spending the per-host budget on two hosts that could not answer. The alias
    /// that did work, <c>tapvizier.u-strasbg.fr</c>, is a CNAME to <c>tapvizier.cds.unistra.fr</c>,
    /// which is the real host and now leads.</para>
    ///
    /// <para>This is the DEFAULT, not the list: it is a setting, because these hostnames have moved
    /// before and a constant in the binary leaves nobody a way to route around the next move.</para>
    /// </summary>
    public static readonly IReadOnlyList<VizierEndpoint> DefaultEndpoints = new[]
    {
        new VizierEndpoint("tapvizier.cds.unistra.fr", "https://tapvizier.cds.unistra.fr/TAPVizieR/tap/sync"),
        new VizierEndpoint("tapvizier.u-strasbg.fr", "https://tapvizier.u-strasbg.fr/TAPVizieR/tap/sync"),
        new VizierEndpoint("vizier.china-vo.org", "http://vizier.china-vo.org/tap/sync"),
    };

    private readonly HttpClient _httpClient;
    private readonly Func<IReadOnlyList<VizierEndpoint>>? _configuredEndpoints;

    public VizierService(HttpClient httpClient, Func<IReadOnlyList<VizierEndpoint>>? configuredEndpoints = null)
    {
        _httpClient = httpClient;
        _configuredEndpoints = configuredEndpoints;
    }

    /// <summary>The mirrors this search will try: the user's list when they have set one, else the default.</summary>
    public IReadOnlyList<VizierEndpoint> Endpoints
    {
        get
        {
            var configured = _configuredEndpoints?.Invoke();
            return configured is { Count: > 0 } ? configured : DefaultEndpoints;
        }
    }

    /// <summary>
    /// Parse a user-supplied mirror list: one URL per line, blank lines and <c>#</c> comments ignored.
    /// The host is taken from the URL, so a caller states one thing rather than two that can disagree.
    /// An unparseable line is dropped rather than failing the list — a typo in the fourth mirror should
    /// not take out the first three.
    /// </summary>
    public static IReadOnlyList<VizierEndpoint> ParseEndpointList(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var list = new List<VizierEndpoint>();
        foreach (var raw in text.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (!Uri.TryCreate(line, UriKind.Absolute, out var uri)) continue;
            if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) continue;
            list.Add(new VizierEndpoint(uri.Host, line));
        }
        return list;
    }

    /// <summary>The default list in the format <see cref="ParseEndpointList"/> reads, for the settings field.</summary>
    public static string FormatEndpointList(IReadOnlyList<VizierEndpoint> endpoints)
        => string.Join(Environment.NewLine, endpoints.Select(e => e.SyncUrl));

    /// <summary>
    /// The canonical VizieR cone-search ADQL. With no <paramref name="columns"/> it is byte-compatible
    /// with the macOS TAPClient (<c>SELECT TOP n *</c>); naming columns narrows the projection, which
    /// is the difference between a usable answer and one nobody can hold — a Gaia DR3 cone is about
    /// 230 columns a row, and 500 rows of that is ~760 KB.
    /// </summary>
    public static string BuildAdql(
        string catalogue, double raDeg, double decDeg, double radiusDeg,
        string raColumn, string decColumn, int maxRec, IReadOnlyList<string>? columns = null)
    {
        var inv = CultureInfo.InvariantCulture;
        var projection = FormatProjection(columns);
        return $"SELECT TOP {maxRec.ToString(inv)} {projection}\n" +
               $"FROM \"{catalogue}\"\n" +
               "WHERE 1 = CONTAINS(\n" +
               $"    POINT('ICRS', {raColumn}, {decColumn}),\n" +
               $"    CIRCLE('ICRS', {raDeg.ToString(inv)}, {decDeg.ToString(inv)}, {radiusDeg.ToString(inv)})\n" +
               ")";
    }

    /// <summary>
    /// The SELECT list: <c>*</c> when no columns are named, else each column quoted so that VizieR's
    /// own names — which carry <c>-</c>, <c>_</c> and mixed case, as in <c>e_RAJ2000</c> and
    /// <c>Gmag</c> — survive as written. A name carrying a quote is dropped rather than escaped:
    /// nothing in a VizieR column name needs one, so it is a caller error, not a value.
    /// </summary>
    private static string FormatProjection(IReadOnlyList<string>? columns)
    {
        if (columns is null || columns.Count == 0) return "*";

        var names = columns
            .Where(c => !string.IsNullOrWhiteSpace(c) && !c.Contains('"'))
            .Select(c => $"\"{c.Trim()}\"")
            .ToList();

        return names.Count == 0 ? "*" : string.Join(", ", names);
    }

    public virtual async Task<(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows)> ConeSearchAsync(
        string catalogue, double raDeg, double decDeg, double radiusDeg,
        string raColumn = "RAJ2000", string decColumn = "DEJ2000", int maxRec = 500,
        IReadOnlyList<string>? columns = null,
        CancellationToken cancellationToken = default)
    {
        var adql = BuildAdql(catalogue, raDeg, decDeg, radiusDeg, raColumn, decColumn, maxRec, columns);

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
    /// failure (DNS, TLS, connection refused) or per-host timeout, and every status except the two
    /// that are definitively about the REQUEST.
    ///
    /// <para>Only 400 (the service read the query and refused it) and 403 (it refused the caller) mean
    /// the next mirror would say the same thing. Everything else rotates — 404 above all, which was
    /// stopping the chain even though it is the status that LEAST indicates a query problem: it means
    /// the TAP path is not on that host, which is exactly what another mirror might fix.</para>
    /// </summary>
    internal static bool IsHostFailoverWorthy(Exception ex) => ex switch
    {
        HttpRequestException h => h.StatusCode is not (System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Forbidden),
        OperationCanceledException => true, // per-host timeout (caller cancellation never reaches here)
        IOException => true,
        _ => false,
    };

    /// <summary>One sync TAP POST against one mirror (same form encoding as <see cref="TAPService"/>).</summary>
    private async Task<string> QueryOnceAsync(string url, string adql, int maxRec, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new[]
        {
            // REQUEST is mandatory on TAPVizieR (400 without it) — CADC's TAP merely tolerates
            // its absence, so don't copy TAPService's form verbatim.
            new KeyValuePair<string, string>("REQUEST", "doQuery"),
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
