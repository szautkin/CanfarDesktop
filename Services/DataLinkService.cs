using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;

namespace CanfarDesktop.Services;

public class DataLinkService
{
    private readonly HttpClient _httpClient;
    private readonly ApiEndpoints _endpoints;
    private readonly ConcurrentDictionary<string, DataLinkResult> _cache = new();
    private static readonly SemaphoreSlim _downloadSemaphore = new(3); // max 3 concurrent image downloads

    public DataLinkService(HttpClient httpClient, ApiEndpoints endpoints)
    {
        _httpClient = httpClient;
        _endpoints = endpoints;
    }

    public async Task<DataLinkResult> GetLinksAsync(string publisherID)
    {
        if (string.IsNullOrWhiteSpace(publisherID))
            return new DataLinkResult();

        if (_cache.TryGetValue(publisherID, out var cached))
            return cached;

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, _endpoints.DataLinkUrl(publisherID));
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/x-votable+xml"));

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return CacheAndReturn(publisherID, new DataLinkResult());

            var xml = await response.Content.ReadAsStringAsync();
            var result = ParseVOTable(xml);
            result.DownloadUrl = _endpoints.DownloadUrl(publisherID);
            return CacheAndReturn(publisherID, result);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DataLink fetch failed for {publisherID}: {ex.Message}");
            return new DataLinkResult(); // Don't cache failures — allow retry
        }
    }

    public string GetDownloadUrl(string publisherID) => _endpoints.DownloadUrl(publisherID);

    public async Task<HttpResponseMessage> DownloadAsync(string url, int timeoutSeconds = 30)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        response.EnsureSuccessStatusCode();
        return response;
    }

    /// <summary>
    /// Download image bytes with timeout, concurrency limit, and single retry.
    /// Returns null on failure. Max 3 concurrent downloads to avoid overwhelming CADC.
    /// </summary>
    public async Task<byte[]?> DownloadImageBytesAsync(string url, int timeoutSeconds = 15)
    {
        await _downloadSemaphore.WaitAsync();
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                    using var response = await _httpClient.GetAsync(url, cts.Token);
                    if (!response.IsSuccessStatusCode) return null;
                    return await response.Content.ReadAsByteArrayAsync(cts.Token);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
                {
                    System.Diagnostics.Debug.WriteLine($"Image download attempt {attempt + 1} failed: {ex.GetType().Name}");
                    if (attempt == 0) await Task.Delay(300);
                }
                catch (IOException)
                {
                    // Connection dropped by CADC — expected under load, don't retry
                    return null;
                }
            }
            return null;
        }
        finally
        {
            _downloadSemaphore.Release();
        }
    }

    private DataLinkResult CacheAndReturn(string key, DataLinkResult result)
    {
        _cache.TryAdd(key, result);
        return result;
    }

    internal static DataLinkResult ParseVOTable(string xml)
    {
        var result = new DataLinkResult();

        // Extract FIELD names to determine column indices
        var fieldNames = new List<string>();
        foreach (Match m in Regex.Matches(xml, @"<FIELD[^>]*name=""([^""]*)""\s*[^>]*/?>", RegexOptions.IgnoreCase))
            fieldNames.Add(m.Groups[1].Value);

        var accessUrlIdx = fieldNames.IndexOf("access_url");
        var semanticsIdx = fieldNames.IndexOf("semantics");
        var contentTypeIdx = fieldNames.IndexOf("content_type");
        var errorIdx = fieldNames.IndexOf("error_message");

        if (accessUrlIdx < 0 || semanticsIdx < 0) return result;

        // Extract rows — handle both <TD>value</TD> and <TD/> (self-closing empty)
        foreach (Match rowMatch in Regex.Matches(xml, @"<TR>(.*?)</TR>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            var cells = ParseTDCells(rowMatch.Groups[1].Value);
            if (cells.Count <= Math.Max(accessUrlIdx, semanticsIdx)) continue;

            // Skip error rows
            if (errorIdx >= 0 && errorIdx < cells.Count && !string.IsNullOrWhiteSpace(cells[errorIdx]))
                continue;

            var url = cells[accessUrlIdx];
            var semantics = cells[semanticsIdx];
            var contentType = contentTypeIdx >= 0 && contentTypeIdx < cells.Count ? cells[contentTypeIdx] : "";

            if (string.IsNullOrWhiteSpace(url)) continue;

            if (semantics == "#thumbnail")
                result.Thumbnails.Add(url);
            else if (semantics == "#preview" && contentType.Contains("image", StringComparison.OrdinalIgnoreCase))
                result.Previews.Add(url);
        }

        System.Diagnostics.Debug.WriteLine($"DataLink parsed: {result.Thumbnails.Count} thumbnails, {result.Previews.Count} previews");
        return result;
    }

    /// <summary>
    /// Parse TD cells handling both <TD>value</TD> and <TD/> (self-closing empty).
    /// </summary>
    private static List<string> ParseTDCells(string rowContent)
    {
        var cells = new List<string>();
        // Match <TD>content</TD> or <TD/> or <TD />
        foreach (Match m in Regex.Matches(rowContent, @"<TD\s*/\s*>|<TD>(.*?)</TD>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            if (m.Value.Contains("/>"))
                cells.Add(""); // self-closing = empty cell
            else
                cells.Add(m.Groups[1].Value.Trim());
        }
        return cells;
    }
}
