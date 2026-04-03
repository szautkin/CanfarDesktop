using CanfarDesktop.Helpers;
using CanfarDesktop.Models;

namespace CanfarDesktop.Services;

public interface ITAPService
{
    Task<SearchResults> ExecuteQueryAsync(string adql, int maxRecords = 10000);
    Task<List<DataTrainRow>> GetDataTrainAsync();
    Task<ResolverResult?> ResolveTargetAsync(string target, string service = "ALL");
}

public class TAPService : ITAPService
{
    private readonly HttpClient _httpClient;
    private readonly ApiEndpoints _endpoints;

    public TAPService(HttpClient httpClient, ApiEndpoints endpoints)
    {
        _httpClient = httpClient;
        _endpoints = endpoints;
    }

    public async Task<SearchResults> ExecuteQueryAsync(string adql, int maxRecords = 10000)
    {
        using var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("LANG", "ADQL"),
            new KeyValuePair<string, string>("FORMAT", "csv"),
            new KeyValuePair<string, string>("MAXREC", maxRecords.ToString()),
            new KeyValuePair<string, string>("QUERY", adql)
        });

        using var response = await _httpClient.PostAsync(_endpoints.TapSyncUrl, content);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"TAP query failed ({(int)response.StatusCode}): {body}");
        }

        var csv = await response.Content.ReadAsStringAsync();
        return ParseCsv(csv, adql);
    }

    public async Task<List<DataTrainRow>> GetDataTrainAsync()
    {
        var adql = """
            SELECT energy_emBand, collection, instrument_name,
                   energy_bandpassName, calibrationLevel, dataProductType, type
            FROM caom2.enumfield
            ORDER BY energy_emBand, collection, instrument_name,
                     energy_bandpassName, calibrationLevel, dataProductType, type
            """;

        using var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("LANG", "ADQL"),
            new KeyValuePair<string, string>("FORMAT", "csv"),
            new KeyValuePair<string, string>("MAXREC", "50000"),
            new KeyValuePair<string, string>("QUERY", adql)
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var response = await _httpClient.PostAsync(_endpoints.TapSyncUrl, content, cts.Token);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cts.Token);
            System.Diagnostics.Debug.WriteLine($"Data train failed: {body}");
            throw new HttpRequestException($"Data train query failed ({(int)response.StatusCode}): {body}");
        }

        var csv = await response.Content.ReadAsStringAsync(cts.Token);
        // Parse off UI thread
        return await Task.Run(() => ParseDataTrainCsv(csv));
    }

    public async Task<ResolverResult?> ResolveTargetAsync(string target, string service = "ALL")
    {
        var url = $"{_endpoints.ResolverUrl}?target={Uri.EscapeDataString(target)}" +
                  $"&service={service}&format=ascii&detail=max&cached=true";

        using var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;

        var text = await response.Content.ReadAsStringAsync();
        return ParseResolverResponse(text, target);
    }

    private static SearchResults ParseCsv(string csv, string? query)
    {
        var result = new SearchResults { Query = query };
        var lines = csv.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return result;

        result.Columns = ParseCsvLine(lines[0]);

        for (var i = 1; i < lines.Length; i++)
        {
            var values = ParseCsvLine(lines[i]);
            if (values.Count != result.Columns.Count)
            {
                System.Diagnostics.Debug.WriteLine($"CSV row {i}: expected {result.Columns.Count} cols, got {values.Count}");
                continue;
            }

            var row = new SearchResultRow();
            for (var j = 0; j < result.Columns.Count; j++)
                row.Values[result.Columns[j]] = values[j];
            result.Rows.Add(row);
        }

        return result;
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

    private static ResolverResult? ParseResolverResponse(string text, string target)
    {
        // ASCII format: key=value lines
        var values = new Dictionary<string, string>();
        foreach (var line in text.Split('\n'))
        {
            var eq = line.IndexOf('=');
            if (eq > 0)
                values[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }

        if (!values.TryGetValue("ra", out var raStr) || !double.TryParse(raStr, out var ra))
            return null;
        if (!values.TryGetValue("dec", out var decStr) || !double.TryParse(decStr, out var dec))
            return null;

        return new ResolverResult
        {
            Target = values.GetValueOrDefault("target", target),
            RA = ra,
            Dec = dec,
            CoordSys = values.GetValueOrDefault("coordsys"),
            ObjectType = values.GetValueOrDefault("oType"),
            Service = values.GetValueOrDefault("service")
        };
    }

    /// <summary>
    /// Lightweight parser: CSV directly to DataTrainRow list.
    /// No intermediate SearchResults/Dictionary objects.
    /// </summary>
    private static List<DataTrainRow> ParseDataTrainCsv(string csv)
    {
        var rows = new List<DataTrainRow>();
        var lines = csv.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return rows; // header + at least 1 data row

        // Skip header (line 0), parse data rows directly by position
        for (var i = 1; i < lines.Length; i++)
        {
            var fields = ParseCsvLine(lines[i]);
            if (fields.Count < 7) continue;

            rows.Add(new DataTrainRow
            {
                Band = fields[0].Trim(),
                Collection = fields[1].Trim(),
                Instrument = fields[2].Trim(),
                Filter = fields[3].Trim(),
                CalibrationLevel = fields[4].Trim(),
                DataProductType = fields[5].Trim(),
                ObservationType = fields[6].Trim(),
                IsFresh = true
            });
        }

        System.Diagnostics.Debug.WriteLine($"ParseDataTrainCsv: {rows.Count} rows from {lines.Length - 1} CSV lines");
        return rows;
    }
}
