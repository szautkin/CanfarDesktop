using System.Net;
using System.Net.Http.Headers;

namespace CanfarDesktop.Services.HttpClients;

/// <summary>
/// DelegatingHandler that automatically adds the Bearer token to every outgoing HTTP request.
/// Detects 401 responses and signals the token provider.
/// </summary>
public class AuthTokenHandler : DelegatingHandler
{
    private readonly AuthTokenProvider _tokenProvider;

    public AuthTokenHandler(AuthTokenProvider tokenProvider)
    {
        _tokenProvider = tokenProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_tokenProvider.Token) && request.Headers.Authorization is null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokenProvider.Token);
        }

        var response = await base.SendAsync(request, cancellationToken);

        // If we sent a token and got 401, signal token expiry
        // Skip for login/whoami endpoints (they handle auth themselves)
        if (response.StatusCode == HttpStatusCode.Unauthorized
            && request.Headers.Authorization is not null
            && request.RequestUri?.AbsolutePath.Contains("/ac/") != true)
        {
            _tokenProvider.RaiseUnauthorized();
        }

        return response;
    }
}
