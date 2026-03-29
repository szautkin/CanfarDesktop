using System.Net;
using Xunit;
using CanfarDesktop.Helpers;
using CanfarDesktop.Services;
using CanfarDesktop.Tests.Helpers;

namespace CanfarDesktop.Tests.Services;

public class SessionServiceTests
{
    private static SessionService CreateService(MockHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var endpoints = new ApiEndpoints();
        return new SessionService(httpClient, endpoints);
    }

    [Fact]
    public async Task RenewSessionAsync_SendsPostWithFormEncodedContentType()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK);
        var service = CreateService(handler);

        await service.RenewSessionAsync("test-123");

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.Contains("/skaha/v1/session/test-123?action=renew", handler.LastRequest.RequestUri!.ToString());
        Assert.NotNull(handler.LastRequest.Content);
        Assert.Equal(
            "application/x-www-form-urlencoded",
            handler.LastRequest.Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task RenewSessionAsync_Success_DoesNotThrow()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK);
        var service = CreateService(handler);

        var exception = await Record.ExceptionAsync(() => service.RenewSessionAsync("test-123"));

        Assert.Null(exception);
    }

    [Fact]
    public async Task RenewSessionAsync_Failure_ThrowsHttpRequestExceptionWithDetails()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.Forbidden, "token expired");
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => service.RenewSessionAsync("test-123"));

        Assert.Contains("403", ex.Message);
        Assert.Contains("token expired", ex.Message);
    }
}
