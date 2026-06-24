using System.Text;
using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Builtin;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

public class ToolTests
{
    private static readonly McpToolContext Ctx = McpToolContext.ForExternal("Claude/1.0", Guid.Empty);

    private static JsonValue DataJson(ToolResult result)
    {
        var data = Assert.IsType<DataResult>(result);
        return JsonValue.Parse(Encoding.UTF8.GetString(data.Json));
    }

    // ── ToolDescriptor ────────────────────────────────────────────────────────

    [Fact]
    public void WithStaticSchema_ThrowsOnInvalidSchema()
        => Assert.Throws<InvalidOperationException>(() =>
            ToolDescriptor.WithStaticSchema("bad", "x", "{not valid json"));

    [Fact]
    public void WithStaticSchema_ParsesValidSchema()
    {
        var d = ToolDescriptor.WithStaticSchema("ok", "desc", """{"type":"object"}""");
        Assert.Equal("ok", d.Name);
        Assert.IsType<JsonObject>(d.InputSchema);
        Assert.Equal("ok", d.ToWire().Name);
    }

    // ── Foundational tools ────────────────────────────────────────────────────

    [Fact]
    public async Task DescribeApp_ReturnsBriefWithVersion()
    {
        var result = await new DescribeAppTool("1.1.0.0").InvokeAsync(JsonValue.Null, Ctx, default);
        var json = (JsonObject)DataJson(result);
        Assert.Equal("1.1.0.0", ((JsonString)json["version"]!).Value);
        Assert.Contains("CADC", ((JsonString)json["summary"]!).Value);
    }

    [Fact]
    public async Task GetAuthState_ReflectsSnapshot()
    {
        var tool = new GetAuthStateTool(() => new AuthSnapshot(true, "alice"));
        var json = (JsonObject)DataJson(await tool.InvokeAsync(JsonValue.Null, Ctx, default));
        Assert.True(((JsonBool)json["isAuthenticated"]!).Value);
        Assert.Equal("alice", ((JsonString)json["username"]!).Value);
    }

    // ── JsonReadTool plumbing ─────────────────────────────────────────────────

    private sealed record DoubleArgs { public int N { get; init; } }

    private sealed class DoubleTool : JsonReadTool<DoubleArgs, DoubleTool.Out>
    {
        public override ToolDescriptor Descriptor { get; } =
            ToolDescriptor.WithStaticSchema("double", "doubles n", """{"type":"object"}""");
        protected override Task<Out> HandleAsync(DoubleArgs a, McpToolContext c, CancellationToken ct)
            => Task.FromResult(new Out(a.N * 2));
        public sealed record Out(int Result);
    }

    [Fact]
    public async Task JsonReadTool_DeserializesArgs_AndSerializesOutput()
    {
        var result = await new DoubleTool().InvokeAsync(JsonValue.Parse("""{"n":21}"""), Ctx, default);
        Assert.Equal(42, ((JsonInt)((JsonObject)DataJson(result))["result"]!).Value);
    }

    private sealed class FailTool : JsonReadTool<EmptyArgs, object>
    {
        public override ToolDescriptor Descriptor { get; } =
            ToolDescriptor.WithStaticSchema("fail", "always fails", """{"type":"object"}""");
        protected override Task<object> HandleAsync(EmptyArgs a, McpToolContext c, CancellationToken ct)
            => throw new McpToolException(new AuthRequired());
    }

    [Fact]
    public async Task JsonReadTool_MapsMcpToolException_ToFailedResult()
    {
        var result = await new FailTool().InvokeAsync(JsonValue.Null, Ctx, default);
        var failed = Assert.IsType<FailedResult>(result);
        Assert.IsType<AuthRequired>(failed.Reason);
        Assert.Equal("auth_required", failed.Reason.AuditTag);
    }

    private sealed class ThrowTool : JsonReadTool<EmptyArgs, object>
    {
        public override ToolDescriptor Descriptor { get; } =
            ToolDescriptor.WithStaticSchema("throw", "throws", """{"type":"object"}""");
        protected override Task<object> HandleAsync(EmptyArgs a, McpToolContext c, CancellationToken ct)
            => throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task JsonReadTool_MapsUnexpectedException_ToBackendError()
    {
        var result = await new ThrowTool().InvokeAsync(JsonValue.Null, Ctx, default);
        Assert.IsType<BackendError>(Assert.IsType<FailedResult>(result).Reason);
    }
}
