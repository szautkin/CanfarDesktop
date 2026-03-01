namespace CanfarDesktop.Models;

/// <summary>
/// Represents the result of an authentication operation.
/// </summary>
public class AuthResult
{
    public bool Success { get; set; }

    public string? Token { get; set; }

    public string? Username { get; set; }

    public string? ErrorMessage { get; set; }
}
