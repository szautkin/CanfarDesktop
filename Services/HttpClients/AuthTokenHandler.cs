using System.Net.Http.Headers;

namespace CanfarDesktop.Services.HttpClients;

/// <summary>
/// DelegatingHandler that automatically adds the Bearer token to every outgoing HTTP request.
/// </summary>
public class AuthTokenHandler : DelegatingHandler
{
    private readonly AuthTokenProvider _tokenProvider;

    public AuthTokenHandler(AuthTokenProvider tokenProvider)
    {
        _tokenProvider = tokenProvider;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_tokenProvider.Token) && request.Headers.Authorization is null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokenProvider.Token);
        }
        return base.SendAsync(request, cancellationToken);
    }
}
