using System.Xml.Linq;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;

namespace CanfarDesktop.Services;

public class StorageService : IStorageService
{
    private readonly HttpClient _httpClient;
    private readonly ApiEndpoints _endpoints;

    public StorageService(HttpClient httpClient, ApiEndpoints endpoints)
    {
        _httpClient = httpClient;
        _endpoints = endpoints;
    }

    public async Task<StorageQuota?> GetQuotaAsync(string username)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, _endpoints.StorageUrl(username));
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/xml"));
        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            System.Diagnostics.Debug.WriteLine($"Storage API {response.StatusCode}: {body}");
            throw new HttpRequestException($"Storage returned {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        var xml = await response.Content.ReadAsStringAsync();
        return ParseVoSpaceXml(xml);
    }

    private static StorageQuota? ParseVoSpaceXml(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            XNamespace vos = "http://www.ivoa.net/xml/VOSpace/v2.0";

            long quota = 0;
            long size = 0;
            string? date = null;

            // Only parse properties from the root node, not from child nodes
            var rootNode = doc.Root;
            var rootProperties = rootNode?.Element(vos + "properties");
            if (rootProperties is null) return null;

            foreach (var prop in rootProperties.Elements(vos + "property"))
            {
                var uri = prop.Attribute("uri")?.Value ?? "";
                var value = prop.Value;

                System.Diagnostics.Debug.WriteLine($"VOSpace property: {uri} = {value}");

                if (uri.Contains("core#quota") && long.TryParse(value, out var q))
                    quota = q;
                else if (uri.Contains("core#length") && long.TryParse(value, out var s))
                    size = s;
                else if (uri.Contains("core#date"))
                    date = value;
            }

            System.Diagnostics.Debug.WriteLine($"Storage parsed: quota={quota}, size={size}, date={date}");

            return new StorageQuota
            {
                QuotaBytes = quota,
                UsedBytes = size,
                LastModified = date
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"VOSpace XML parse error: {ex}");
            return null;
        }
    }
}
