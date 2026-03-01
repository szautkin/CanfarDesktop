using System.Net.Http.Headers;
using System.Text.Json;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Services.HttpClients;

namespace CanfarDesktop.Services;

public class AuthService : IAuthService
{
    private readonly HttpClient _httpClient;
    private readonly ApiEndpoints _endpoints;
    private readonly TokenStorage _tokenStorage;
    private readonly AuthTokenProvider _tokenProvider;

    public bool IsAuthenticated { get; private set; }
    public string? CurrentToken => _tokenProvider.Token;
    public string? CurrentUsername { get; private set; }

    public AuthService(HttpClient httpClient, ApiEndpoints endpoints, TokenStorage tokenStorage, AuthTokenProvider tokenProvider)
    {
        _httpClient = httpClient;
        _endpoints = endpoints;
        _tokenStorage = tokenStorage;
        _tokenProvider = tokenProvider;
    }

    public async Task<AuthResult> LoginAsync(string username, string password, bool rememberMe = true)
    {
        try
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("username", username),
                new KeyValuePair<string, string>("password", password)
            });

            var response = await _httpClient.PostAsync(_endpoints.LoginUrl, content);
            if (!response.IsSuccessStatusCode)
            {
                return new AuthResult
                {
                    Success = false,
                    ErrorMessage = response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                        ? "Invalid username or password."
                        : $"Login failed: {response.StatusCode}"
                };
            }

            var token = (await response.Content.ReadAsStringAsync()).Trim();
            _tokenProvider.Token = token;
            CurrentUsername = username;
            IsAuthenticated = true;

            if (rememberMe)
                _tokenStorage.SaveToken(token, username);
            else
                _tokenStorage.ClearToken();

            return new AuthResult { Success = true, Token = token, Username = username };
        }
        catch (HttpRequestException ex)
        {
            return new AuthResult { Success = false, ErrorMessage = $"Network error: {ex.Message}" };
        }
    }

    public async Task<string?> ValidateTokenAsync(string token)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, _endpoints.WhoAmIUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var username = (await response.Content.ReadAsStringAsync()).Trim();
                _tokenProvider.Token = token;
                CurrentUsername = username;
                IsAuthenticated = true;
                return username;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<UserInfo?> GetUserInfoAsync(string username)
    {
        try
        {
            var response = await _httpClient.GetAsync(_endpoints.UserUrl(username));
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<UserInfo>(json);
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    public Task LogoutAsync()
    {
        _tokenProvider.Clear();
        CurrentUsername = null;
        IsAuthenticated = false;
        _tokenStorage.ClearToken();
        return Task.CompletedTask;
    }
}
