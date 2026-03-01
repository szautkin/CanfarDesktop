using System.Text.Json;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;

namespace CanfarDesktop.Services;

public class PlatformService : IPlatformService
{
    private readonly HttpClient _httpClient;
    private readonly ApiEndpoints _endpoints;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public PlatformService(HttpClient httpClient, ApiEndpoints endpoints)
    {
        _httpClient = httpClient;
        _endpoints = endpoints;
    }

    public async Task<SkahaStatsResponse?> GetStatsAsync()
    {
        var response = await _httpClient.GetAsync(_endpoints.StatsUrl);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            System.Diagnostics.Debug.WriteLine($"Stats API {response.StatusCode}: {body}");
            throw new HttpRequestException($"Stats returned {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<SkahaStatsResponse>(json, JsonOptions);
    }
}
