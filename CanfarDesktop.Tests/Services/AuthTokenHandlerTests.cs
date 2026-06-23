using System.Net;
using System.Net.Http.Headers;
using Xunit;
using CanfarDesktop.Services.HttpClients;

namespace CanfarDesktop.Tests.Services;

public class AuthTokenHandlerTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Last;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Last = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static async Task<AuthenticationHeaderValue?> SendAndGetAuth(string url, string? token)
    {
        var provider = new AuthTokenProvider { Token = token };
        var inner = new CapturingHandler();
        var handler = new AuthTokenHandler(provider) { InnerHandler = inner };
        using var invoker = new HttpMessageInvoker(handler);
        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, url), CancellationToken.None);
        return inner.Last!.Headers.Authorization;
    }

    [Fact]
    public async Task AttachesToken_ForTrustedHttpsHost()
    {
        var auth = await SendAndGetAuth("https://ws-uv.canfar.net/skaha/v1/session", "tok123");
        Assert.NotNull(auth);
        Assert.Equal("Bearer", auth!.Scheme);
        Assert.Equal("tok123", auth.Parameter);
    }

    [Fact]
    public async Task DoesNotAttach_ForUntrustedHost()
    {
        var auth = await SendAndGetAuth("https://example.com/steal", "tok123");
        Assert.Null(auth);
    }

    [Fact]
    public async Task DoesNotAttach_OverHttp()
    {
        var auth = await SendAndGetAuth("http://ws-uv.canfar.net/skaha", "tok123");
        Assert.Null(auth);
    }

    [Fact]
    public async Task DoesNotAttach_WhenNoToken()
    {
        var auth = await SendAndGetAuth("https://ws-uv.canfar.net/skaha", null);
        Assert.Null(auth);
    }
}
