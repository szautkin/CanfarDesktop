namespace CanfarDesktop.Services.HttpClients;

/// <summary>
/// Singleton that holds the current auth token in memory.
/// All HttpClients read from this to attach Bearer headers.
/// </summary>
public class AuthTokenProvider
{
    public string? Token { get; set; }

    public void Clear() => Token = null;
}
