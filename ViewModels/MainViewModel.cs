using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Services;

namespace CanfarDesktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private readonly TokenStorage _tokenStorage;

    [ObservableProperty]
    private bool _isAuthenticated;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private UserInfo? _userInfo;

    /// <summary>
    /// Raised when token expires mid-session and silent re-auth fails.
    /// MainWindow should show the login dialog.
    /// </summary>
    public event EventHandler? TokenExpired;

    public MainViewModel(IAuthService authService, TokenStorage tokenStorage)
    {
        _authService = authService;
        _tokenStorage = tokenStorage;
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        StatusMessage = "Checking authentication...";

        var (token, username) = _tokenStorage.LoadToken();
        if (token is not null && username is not null)
        {
            var validatedUser = await _authService.ValidateTokenAsync(token);
            if (validatedUser is not null)
            {
                Username = validatedUser;
                IsAuthenticated = true;
                UserInfo = await _authService.GetUserInfoAsync(validatedUser);
                StatusMessage = $"Welcome, {validatedUser}";
            }
            else
            {
                // Token expired — try silent re-auth before prompting
                if (await TrySilentReauthAsync())
                {
                    IsLoading = false;
                    return;
                }
                _tokenStorage.ClearToken();
                StatusMessage = "Session expired. Please log in.";
            }
        }
        else
        {
            StatusMessage = "Please log in to continue.";
        }

        IsLoading = false;
    }

    public void UpdateAuthState(string username, UserInfo? userInfo)
    {
        Username = username;
        UserInfo = userInfo;
        IsAuthenticated = true;
        StatusMessage = $"Welcome, {username}";
    }

    private bool _isReAuthInProgress;
    private DateTimeOffset _lastReauthFailure = DateTimeOffset.MinValue;

    /// <summary>
    /// The ONE owner of mid-session 401 handling (every authenticated HttpClient funnels here via
    /// AuthTokenHandler → MainWindow). Attempts silent re-auth with stored credentials; raises
    /// TokenExpired when that fails so the shell can show the persistent sign-in bar.
    /// Deliberately NOT gated on IsAuthenticated: after one failed attempt flipped it false,
    /// widgets kept polling with the dead token and every later 401 became a no-op — the user
    /// stared at raw 401s with no way back in. A cooldown keeps failed attempts from hammering
    /// the login endpoint on every poll tick; the sign-in bar owns the UX between attempts.
    /// </summary>
    public async Task HandleTokenExpiredAsync()
    {
        if (_isReAuthInProgress) return;
        if (DateTimeOffset.UtcNow - _lastReauthFailure < TimeSpan.FromSeconds(60)) return;
        _isReAuthInProgress = true;

        try
        {
            if (await TrySilentReauthAsync())
            {
                _lastReauthFailure = DateTimeOffset.MinValue;
                return;
            }

            // Silent re-auth failed — reset state and notify UI
            _lastReauthFailure = DateTimeOffset.UtcNow;
            IsAuthenticated = false;
            Username = string.Empty;
            UserInfo = null;
            StatusMessage = "Session expired. Please log in again.";
            TokenExpired?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _isReAuthInProgress = false;
        }
    }

    private async Task<bool> TrySilentReauthAsync()
    {
        var (storedUser, storedPass) = _tokenStorage.LoadCredentials();
        if (storedUser is null || storedPass is null) return false;

        StatusMessage = "Renewing session...";

        var result = await _authService.LoginAsync(storedUser, storedPass, rememberMe: true);
        if (result.Success)
        {
            var user = result.Username ?? storedUser;
            var info = await _authService.GetUserInfoAsync(user);
            UpdateAuthState(user, info);
            return true;
        }

        // Credentials no longer valid
        _tokenStorage.ClearToken();
        return false;
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        await _authService.LogoutAsync();
        IsAuthenticated = false;
        Username = string.Empty;
        UserInfo = null;
        StatusMessage = "Logged out.";
    }
}
