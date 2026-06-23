namespace CanfarDesktop.Services.HttpClients;

/// <summary>
/// Singleton that holds the current auth token in memory.
/// All HttpClients read from this to attach Bearer headers.
/// Thread-safe: token may be read/written from multiple HttpClient threads.
/// </summary>
public class AuthTokenProvider
{
    private volatile string? _token;
    private volatile string? _username;

    public string? Token
    {
        get => _token;
        set => _token = value;
    }

    /// <summary>
    /// The signed-in username. Lives here (singleton) rather than on the typed-HttpClient
    /// <c>AuthService</c> (which is transient, so its instance state is discarded between
    /// resolutions) so every consumer — including the image-discovery coordinator — sees it.
    /// </summary>
    public string? Username
    {
        get => _username;
        set => _username = value;
    }

    /// <summary>
    /// Raised when a 401 response is detected on a request that had a Bearer token.
    /// </summary>
    public event EventHandler? Unauthorized;

    public void Clear()
    {
        _token = null;
        _username = null;
    }

    internal void RaiseUnauthorized() => Unauthorized?.Invoke(this, EventArgs.Empty);
}
