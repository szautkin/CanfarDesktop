using System.Text.Json;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;

namespace CanfarDesktop.Services;

public class SessionService : ISessionService
{
    private readonly HttpClient _httpClient;
    private readonly ApiEndpoints _endpoints;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public SessionService(HttpClient httpClient, ApiEndpoints endpoints)
    {
        _httpClient = httpClient;
        _endpoints = endpoints;
    }

    public async Task<List<Session>> GetSessionsAsync()
    {
        var response = await _httpClient.GetAsync(_endpoints.SessionsUrl);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var raw = JsonSerializer.Deserialize<List<SkahaSessionResponse>>(json, JsonOptions) ?? [];
        return raw.Select(MapSession).ToList();
    }

    public async Task<Session?> GetSessionAsync(string id)
    {
        var response = await _httpClient.GetAsync(_endpoints.SessionUrl(id));
        if (!response.IsSuccessStatusCode) return null;
        var json = await response.Content.ReadAsStringAsync();
        var raw = JsonSerializer.Deserialize<SkahaSessionResponse>(json, JsonOptions);
        return raw is not null ? MapSession(raw) : null;
    }

    public async Task<string?> LaunchSessionAsync(SessionLaunchParams launchParams)
    {
        var formData = new List<KeyValuePair<string, string>>
        {
            new("name", launchParams.Name),
            new("image", launchParams.Image),
            new("type", launchParams.Type)
        };

        // Only include cores/ram/gpus for fixed resources — omit entirely for flexible
        if (launchParams.Cores > 0)
            formData.Add(new("cores", launchParams.Cores.ToString()));
        if (launchParams.Ram > 0)
            formData.Add(new("ram", launchParams.Ram.ToString()));
        if (launchParams.Gpus > 0)
            formData.Add(new("gpus", launchParams.Gpus.ToString()));

        if (!string.IsNullOrEmpty(launchParams.Cmd))
            formData.Add(new("cmd", launchParams.Cmd));

        System.Diagnostics.Debug.WriteLine($"Launching session: {string.Join(", ", formData.Select(kv => $"{kv.Key}={kv.Value}"))}");

        var content = new FormUrlEncodedContent(formData);
        var request = new HttpRequestMessage(HttpMethod.Post, _endpoints.SessionsUrl) { Content = content };

        // Registry auth sent as header: x-skaha-registry-auth: base64(username:secret)
        if (!string.IsNullOrEmpty(launchParams.RegistryUsername))
        {
            var authValue = Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes($"{launchParams.RegistryUsername}:{launchParams.RegistrySecret ?? ""}"));
            request.Headers.Add("x-skaha-registry-auth", authValue);
        }

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            System.Diagnostics.Debug.WriteLine($"Launch failed {response.StatusCode}: {errorBody}");
            throw new HttpRequestException($"Launch failed: {(int)response.StatusCode} {response.ReasonPhrase} - {errorBody}");
        }

        var body = (await response.Content.ReadAsStringAsync()).Trim();
        if (body.StartsWith("["))
        {
            var ids = JsonSerializer.Deserialize<string[]>(body);
            return ids?.FirstOrDefault();
        }
        return body;
    }

    public async Task<bool> DeleteSessionAsync(string id)
    {
        var response = await _httpClient.DeleteAsync(_endpoints.SessionUrl(id));
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> RenewSessionAsync(string id)
    {
        var response = await _httpClient.PostAsync(_endpoints.SessionRenewUrl(id), null);
        return response.IsSuccessStatusCode;
    }

    public async Task<string?> GetSessionEventsAsync(string id)
    {
        var response = await _httpClient.GetAsync(_endpoints.SessionEventsUrl(id));
        return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync() : null;
    }

    public async Task<string?> GetSessionLogsAsync(string id)
    {
        var response = await _httpClient.GetAsync(_endpoints.SessionLogsUrl(id));
        return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync() : null;
    }

    private static Session MapSession(SkahaSessionResponse raw) => new()
    {
        Id = raw.Id,
        SessionType = raw.Type,
        SessionName = raw.Name,
        Status = raw.Status,
        ContainerImage = raw.Image,
        StartedTime = raw.StartTime,
        ExpiresTime = raw.ExpiryTime,
        ConnectUrl = raw.ConnectURL,
        MemoryAllocated = raw.RequestedRAM ?? "",
        MemoryUsage = raw.RamInUse,
        CpuAllocated = raw.RequestedCPUCores ?? "",
        CpuUsage = raw.CpuCoresInUse,
        GpuAllocated = raw.RequestedGPUCores,
        IsFixedResources = raw.IsFixedResources ?? true,
        RequestedRAM = raw.RequestedRAM,
        RequestedCPU = raw.RequestedCPUCores,
        RequestedGPU = raw.RequestedGPUCores
    };
}
