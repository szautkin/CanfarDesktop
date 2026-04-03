namespace CanfarDesktop.Services.HttpClients;

/// <summary>
/// Singleton that holds the current auth token in memory.
/// All HttpClients read from this to attach Bearer headers.
/// Thread-safe: token may be read/written from multiple HttpClient threads.
/// </summary>
public class AuthTokenProvider
{
    private volatile string? _token;

    public string? Token
    {
        get => _token;
        set => _token = value;
    }

    /// <summary>
    /// Raised when a 401 response is detected on a request that had a Bearer token.
    /// </summary>
    public event EventHandler? Unauthorized;

    public void Clear() => _token = null;

    internal void RaiseUnauthorized() => Unauthorized?.Invoke(this, EventArgs.Empty);
}
