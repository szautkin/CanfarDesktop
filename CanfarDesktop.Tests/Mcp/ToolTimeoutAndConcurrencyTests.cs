using System.Diagnostics;
using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Transport;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

public class ToolTimeoutAndConcurrencyTests
{
    private static readonly McpToolContext Ctx = McpToolContext.ForExternal("c1", Guid.Empty);

    private const string EmptySchema = """{"type":"object","properties":{},"additionalProperties":false}""";

    // A tool whose handler IGNORES the cancellation token and runs far longer than its tiny timeout —
    // models a backend call with no CancellationToken (the search_observations / list_sessions case).
    private sealed class UncancellableSlowTool : JsonReadTool<EmptyArgs, UncancellableSlowTool.Output>
    {
        private readonly TimeSpan _work;
        public UncancellableSlowTool(TimeSpan timeout, TimeSpan work) { _timeout = timeout; _work = work; }
        private readonly TimeSpan _timeout;
        protected override TimeSpan Timeout => _timeout;
        public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema("slow", "slow", EmptySchema);
        protected override async Task<Output> HandleAsync(EmptyArgs args, McpToolContext context, CancellationToken ct)
        {
            await Task.Delay(_work, CancellationToken.None); // deliberately ignores ct
            return new Output("done");
        }
        public sealed record Output(string Value);
    }

    [Fact]
    public async Task Timeout_FiresEvenWhenHandlerIgnoresToken()
    {
        var tool = new UncancellableSlowTool(timeout: TimeSpan.FromMilliseconds(60), work: TimeSpan.FromSeconds(5));

        var sw = Stopwatch.StartNew();
        var result = await tool.InvokeAsync(JsonValue.Null, Ctx, default);
        sw.Stop();

        Assert.IsType<UpstreamTimeout>(Assert.IsType<FailedResult>(result).Reason);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"should time out fast, took {sw.Elapsed}");
    }

    // ── Concurrent serve loop: a slow call must not block a fast one ──────────────────────────────

    private static IMcpTool Delay(string name, TimeSpan delay) => new NamedDelayTool(name, delay);

    private sealed class NamedDelayTool : JsonReadTool<EmptyArgs, NamedDelayTool.Output>
    {
        private readonly TimeSpan _delay;
        public NamedDelayTool(string name, TimeSpan delay)
        {
            _delay = delay;
            Descriptor = ToolDescriptor.WithStaticSchema(name, name, EmptySchema);
        }
        public override ToolDescriptor Descriptor { get; }
        protected override async Task<Output> HandleAsync(EmptyArgs args, McpToolContext context, CancellationToken ct)
        {
            if (_delay > TimeSpan.Zero) await Task.Delay(_delay, ct);
            return new Output(Descriptor.Name);
        }
        public sealed record Output(string Value);
    }

    private static string Init => """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"t","version":"1"}}}""";
    private static string Call(int id, string name)
        => "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"method\":\"tools/call\",\"params\":{\"name\":\"" + name + "\",\"arguments\":{}}}";

    [Fact]
    public async Task SlowCall_DoesNotBlock_FastCall()
    {
        var router = new McpToolRouter(new[] { Delay("slow", TimeSpan.FromMilliseconds(800)), Delay("fast", TimeSpan.Zero) });
        var server = new McpServerService(router, new ServerIdentity("t", "1"));
        var transport = new InMemoryTransport();
        var serve = server.ServeAsync(transport);

        transport.Inject(Init);
        await Read(transport); // initialize ack

        transport.Inject(Call(2, "slow")); // dispatched first, 800ms
        transport.Inject(Call(3, "fast")); // dispatched right after, instant

        var first = JsonDocument.Parse(await Read(transport)).RootElement;
        Assert.Equal(3, first.GetProperty("id").GetInt32()); // FAST returns first → proves concurrency

        var second = JsonDocument.Parse(await Read(transport)).RootElement;
        Assert.Equal(2, second.GetProperty("id").GetInt32());

        transport.CompleteIncoming();
        await serve;
    }

    private static async Task<string> Read(InMemoryTransport t)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        return (await t.ReadResponseAsync(cts.Token))!;
    }
}
