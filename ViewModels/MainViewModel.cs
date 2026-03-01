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
