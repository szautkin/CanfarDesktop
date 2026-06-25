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

    public async Task<StorageQuota?> GetQuotaAsync(string username, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, _endpoints.StorageUrl(username));
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/xml"));
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        var xml = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            System.Diagnostics.Debug.WriteLine($"Storage API {response.StatusCode}: {xml}");
            throw new HttpRequestException($"Storage returned {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        return ParseVoSpaceXml(xml);
    }

    public async Task<List<VoSpaceNode>> ListNodesAsync(string path, int? limit = null, CancellationToken cancellationToken = default)
    {
        var url = _endpoints.StorageNodeListUrl(path, limit);
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/xml"));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var xml = await response.Content.ReadAsStringAsync(cancellationToken);
        return VoSpaceParser.ParseNodeList(xml);
    }

    public async Task UploadFileAsync(string remotePath, Stream content, string? contentType = null, CancellationToken cancellationToken = default)
    {
        var url = _endpoints.StorageFilesUrl(remotePath);
        using var streamContent = new StreamContent(content);
        if (!string.IsNullOrEmpty(contentType))
            streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        using var response = await _httpClient.PutAsync(url, streamContent, cancellationToken);
        if (!response.IsSuccessStatusCode)
            CanfarDesktop.Helpers.CrashLogger.Info($"[Storage] PUT {url} -> {(int)response.StatusCode} {response.ReasonPhrase}");
        response.EnsureSuccessStatusCode();
    }

    public async Task<Stream> DownloadFileAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        var url = _endpoints.StorageFilesUrl(remotePath);
        try
        {
            return await _httpClient.GetStreamAsync(url, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            CanfarDesktop.Helpers.CrashLogger.Info($"[Storage] GET {url} -> FAILED {(int?)ex.StatusCode} {ex.StatusCode}: {ex.Message}");
            throw;
        }
    }

    public async Task CreateFolderAsync(string remotePath, string folderName, CancellationToken cancellationToken = default)
    {
        if (folderName.Contains("..") || folderName.Contains('/') || folderName.Contains('\\'))
            throw new ArgumentException("Folder name contains invalid characters.");

        var fullPath = string.IsNullOrEmpty(remotePath)
            ? folderName
            : $"{remotePath}/{folderName}";
        var nodeUri = $"vos://cadc.nrc.ca~arc/home/{fullPath}";
        var url = _endpoints.StorageNodeUrl(fullPath);

        var xml = VoSpaceParser.BuildContainerNodeXml(nodeUri);
        using var xmlContent = new StringContent(xml, System.Text.Encoding.UTF8, "text/xml");
        using var response = await _httpClient.PutAsync(url, xmlContent, cancellationToken);
        if (!response.IsSuccessStatusCode)
            CanfarDesktop.Helpers.CrashLogger.Info($"[Storage] MKDIR(node) PUT {url} -> {(int)response.StatusCode} {response.ReasonPhrase}");
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteNodeAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        var url = _endpoints.StorageNodeUrl(remotePath);
        using var response = await _httpClient.DeleteAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
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
