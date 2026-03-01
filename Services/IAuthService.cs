using CanfarDesktop.Models;

namespace CanfarDesktop.Services;

public interface IAuthService
{
    Task<AuthResult> LoginAsync(string username, string password, bool rememberMe = true);
    Task<string?> ValidateTokenAsync(string token);
    Task<UserInfo?> GetUserInfoAsync(string username);
    Task LogoutAsync();
    bool IsAuthenticated { get; }
    string? CurrentToken { get; }
    string? CurrentUsername { get; }
}
