using System.Net;

namespace CanfarDesktop.Tests.Helpers;

public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

    public HttpRequestMessage? LastRequest { get; private set; }

    public MockHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
    {
        _handler = handler;
    }

    public MockHttpMessageHandler(HttpStatusCode statusCode, string content = "")
        : this(_ => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content)
        }))
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        return await _handler(request);
    }
}
