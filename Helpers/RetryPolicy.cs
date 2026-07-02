namespace CanfarDesktop.Helpers;

/// <summary>
/// Generic retry-with-backoff policy for transient network failures. CADC services occasionally
/// return 503 (load balancer / temporarily unavailable), and Wi-Fi → Ethernet transitions drop
/// requests mid-flight; a short backoff retry resolves most of these without the user noticing.
/// Conservative: only 5xx and connection-level failures are transient — 4xx means the request
/// itself is wrong and retrying won't fix it. 1-to-1 with the macOS RetryPolicy.
/// </summary>
public sealed record RetryPolicy(
    int MaxAttempts = 3,
    double InitialDelaySeconds = 0.3,
    double MaxDelaySeconds = 5.0,
    double BackoffMultiplier = 2.0)
{
    /// <summary>Conservative default for short-lived metadata calls.</summary>
    public static readonly RetryPolicy Default = new();

    /// <summary>Next backoff delay after <paramref name="currentSeconds"/>: exponential scaling,
    /// clamped to the max and floored at zero (a negative multiplier must never produce a negative sleep).</summary>
    public double NextDelaySeconds(double currentSeconds)
        => Math.Min(Math.Max(0, currentSeconds * BackoffMultiplier), MaxDelaySeconds);

    /// <summary>True for HTTP statuses worth retrying (server-side transient failures).</summary>
    public static bool IsTransientStatus(int statusCode) => statusCode is >= 500 and < 600;
}
