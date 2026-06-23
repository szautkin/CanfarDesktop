using System.Net;
using Xunit;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Services;

namespace CanfarDesktop.Tests.Services;

public class SessionServiceHeadlessTests
{
    private sealed class SequencedHandler : HttpMessageHandler
    {
        private readonly Func<int, HttpResponseMessage> _responder;
        private int _count;
        public List<string> Bodies { get; } = new();

        public SequencedHandler(Func<int, HttpResponseMessage> responder) => _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            var n = System.Threading.Interlocked.Increment(ref _count);
            return _responder(n);
        }
    }

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    private static HttpResponseMessage Err(HttpStatusCode code, string body) => new(code) { Content = new StringContent(body) };

    private static SessionService Make(SequencedHandler handler) => new(new HttpClient(handler), new ApiEndpoints());

    [Fact]
    public async Task SingleReplica_ReturnsIdAndSendsHeadlessForm()
    {
        var handler = new SequencedHandler(_ => Ok("xyz789\n"));
        var ids = await Make(handler).LaunchHeadlessAsync(new SessionLaunchParams
        {
            Name = "smoke-job", Image = "img", Cmd = "echo hello",
            Env = new() { new("FOO", "bar") }, Replicas = 1,
        });

        Assert.Equal(new[] { "xyz789" }, ids);
        Assert.Single(handler.Bodies);
        Assert.Contains("type=headless", handler.Bodies[0]);
        Assert.Contains("REPLICA_ID%3D1", handler.Bodies[0]); // env=REPLICA_ID=1, URL-encoded
    }

    [Fact]
    public async Task ThreeReplicas_IncrementalNames()
    {
        var handler = new SequencedHandler(n => Ok($"job-{n}\n"));
        var ids = await Make(handler).LaunchHeadlessAsync(new SessionLaunchParams
        {
            Name = "batch", Image = "img", Cmd = "true", Replicas = 3,
        });

        Assert.Equal(new[] { "job-1", "job-2", "job-3" }, ids);
        Assert.Equal(3, handler.Bodies.Count);
        Assert.Contains("name=batch-1", handler.Bodies[0]);
        Assert.Contains("name=batch-2", handler.Bodies[1]);
        Assert.Contains("name=batch-3", handler.Bodies[2]);
    }

    [Fact]
    public async Task PartialFailure_ThrowsWithLaunchedIds()
    {
        var handler = new SequencedHandler(n => n == 3 ? Err(HttpStatusCode.InternalServerError, "kaboom") : Ok($"ok-{n}\n"));
        var ex = await Assert.ThrowsAsync<HeadlessLaunchException>(
            () => Make(handler).LaunchHeadlessAsync(new SessionLaunchParams { Name = "batch", Image = "img", Cmd = "true", Replicas = 5 }));

        Assert.Equal(new[] { "ok-1", "ok-2" }, ex.LaunchedIds);
        Assert.Equal(2, ex.FailedAtIndex);
    }

    [Fact]
    public async Task FirstReplicaFails_ThrowsHttpRequestException()
    {
        var handler = new SequencedHandler(_ => Err(HttpStatusCode.InternalServerError, "nope"));
        await Assert.ThrowsAsync<HttpRequestException>(
            () => Make(handler).LaunchHeadlessAsync(new SessionLaunchParams { Name = "x", Image = "img", Replicas = 2 }));
    }
}
