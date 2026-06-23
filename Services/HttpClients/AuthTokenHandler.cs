using System.Net;
using System.Net.Http.Headers;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Services.HttpClients;

/// <summary>
/// DelegatingHandler that attaches the Bearer token to outgoing requests bound for
/// trusted CANFAR/CADC hosts only (see <see cref="TrustedHosts"/>) and signals the
/// token provider on 401.
///
/// Security: the token is attached only when the request URI is HTTPS and on the
/// trusted-host allowlist, so a server-supplied off-domain URL (e.g. a DataLink
/// access_url pointing at a partner archive) never receives the CADC token. On
/// redirects, .NET's SocketsHttpHandler clears the Authorization header and refuses
/// HTTPS-&gt;HTTP, so the token is not forwarded across hops either.
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
        if (!string.IsNullOrEmpty(_tokenProvider.Token)
            && request.Headers.Authorization is null
            && TrustedHosts.IsTrusted(request.RequestUri))
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
