using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CanfarDesktop.Models;
using CanfarDesktop.Services;

namespace CanfarDesktop.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly IAuthService _authService;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _isLoggingIn;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _rememberMe = true;

    public event Action<string, UserInfo?>? LoginSucceeded;

    public LoginViewModel(IAuthService authService)
    {
        _authService = authService;
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Username and password are required.";
            HasError = true;
            return;
        }

        IsLoggingIn = true;
        HasError = false;
        ErrorMessage = string.Empty;

        var result = await _authService.LoginAsync(Username, Password, RememberMe);

        if (result.Success)
        {
            var userInfo = await _authService.GetUserInfoAsync(Username);
            LoginSucceeded?.Invoke(Username, userInfo);
            Password = string.Empty;
            HasError = false;
        }
        else
        {
            ErrorMessage = result.ErrorMessage ?? "Login failed.";
            HasError = true;
        }

        IsLoggingIn = false;
    }
}
