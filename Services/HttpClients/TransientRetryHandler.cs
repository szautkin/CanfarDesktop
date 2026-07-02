using CanfarDesktop.Helpers;

namespace CanfarDesktop.Services.HttpClients;

/// <summary>
/// DelegatingHandler that retries idempotent requests (GET/HEAD only — a launch or upload must never
/// be blind-retried) on 5xx responses and connection-level failures, with the shared
/// <see cref="RetryPolicy"/> backoff. Registered OUTSIDE the auth handler and clears the stale
/// Authorization header before each retry, so the inner auth handler re-injects the current bearer
/// token per attempt. Mirrors the macOS NetworkClient retrying() integration.
/// </summary>
public sealed class TransientRetryHandler : DelegatingHandler
{
    private readonly RetryPolicy _policy;

    public TransientRetryHandler() : this(RetryPolicy.Default) { }

    public TransientRetryHandler(RetryPolicy policy) => _policy = policy;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
            return await base.SendAsync(request, cancellationToken);

        var attempts = Math.Max(1, _policy.MaxAttempts);
        var delay = _policy.InitialDelaySeconds;
        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage? response = null;
            try
            {
                response = await base.SendAsync(request, cancellationToken);
                if (attempt >= attempts || !RetryPolicy.IsTransientStatus((int)response.StatusCode))
                    return response;
                response.Dispose(); // consumed by the retry — release the connection
            }
            catch (HttpRequestException) when (attempt < attempts && !cancellationToken.IsCancellationRequested)
            {
                // Connection-level failure (DNS, reset, unreachable) — retry after backoff.
            }
            catch
            {
                response?.Dispose();
                throw;
            }

            await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken);
            delay = _policy.NextDelaySeconds(delay);
            // Drop the previous attempt's token so the inner auth handler injects a fresh one
            // (it only sets the header when absent).
            request.Headers.Authorization = null;
        }
    }
}
