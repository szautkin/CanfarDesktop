using System.Text.Json;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;

namespace CanfarDesktop.Services;

public class ImageService : IImageService
{
    private readonly HttpClient _httpClient;
    private readonly ApiEndpoints _endpoints;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private List<RawImage>? _cachedImages;
    private DateTime _cacheTime;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public ImageService(HttpClient httpClient, ApiEndpoints endpoints)
    {
        _httpClient = httpClient;
        _endpoints = endpoints;
    }

    public async Task<List<RawImage>> GetImagesAsync()
    {
        if (_cachedImages is not null && DateTime.UtcNow - _cacheTime < CacheDuration)
            return _cachedImages;

        var response = await _httpClient.GetAsync(_endpoints.ImagesUrl);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        _cachedImages = JsonSerializer.Deserialize<List<RawImage>>(json, JsonOptions) ?? [];
        _cacheTime = DateTime.UtcNow;
        return _cachedImages;
    }

    public async Task<SessionContext?> GetContextAsync()
    {
        var response = await _httpClient.GetAsync(_endpoints.ContextUrl);
        if (!response.IsSuccessStatusCode) return null;
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<SessionContext>(json, JsonOptions);
    }

    public async Task<List<string>> GetRepositoriesAsync()
    {
        var response = await _httpClient.GetAsync(_endpoints.RepositoryUrl);
        if (!response.IsSuccessStatusCode) return [];
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
    }
}
