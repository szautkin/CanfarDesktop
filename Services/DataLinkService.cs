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
            return CacheAndReturn(publisherID, new DataLinkResult());
        }
    }

    public string GetDownloadUrl(string publisherID) => _endpoints.DownloadUrl(publisherID);

    private DataLinkResult CacheAndReturn(string key, DataLinkResult result)
    {
        _cache.TryAdd(key, result);
        return result;
    }

    private static DataLinkResult ParseVOTable(string xml)
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

        // Extract rows
        foreach (Match rowMatch in Regex.Matches(xml, @"<TR>(.*?)</TR>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            var cells = new List<string>();
            foreach (Match tdMatch in Regex.Matches(rowMatch.Groups[1].Value, @"<TD>(.*?)</TD>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
                cells.Add(tdMatch.Groups[1].Value.Trim());

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

        return result;
    }
}
