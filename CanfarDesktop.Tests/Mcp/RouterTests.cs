using Xunit;
using CanfarDesktop.Mcp;
using CanfarDesktop.Mcp.Audit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Builtin;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

public class RouterTests
{
    private static IMcpTool Describe() => new DescribeAppTool("1.0");

    private sealed class UserOnlyTool : IMcpTool
    {
        public McpVerbClass VerbClass => McpVerbClass.Destructive;
        public bool AgentSafe => false;
        public ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema("user_only", "user-only", """{"type":"object"}""");
        public Task<ToolResult> InvokeAsync(JsonValue a, McpToolContext c, CancellationToken ct) => Task.FromResult(ToolResult.Ok(new byte[] { (byte)'1' }));
    }

    /// <summary>An agent-safe read tool returning a fixed result, for exercising the activity callback.</summary>
    private sealed class FakeReadTool : IMcpTool
    {
        private readonly ToolResult _result;
        public FakeReadTool(string name, ToolResult result)
        {
            Descriptor = ToolDescriptor.WithStaticSchema(name, name, """{"type":"object"}""");
            _result = result;
        }
        public McpVerbClass VerbClass => McpVerbClass.Read;
        public bool AgentSafe => true;
        public ToolDescriptor Descriptor { get; }
        public Task<ToolResult> InvokeAsync(JsonValue a, McpToolContext c, CancellationToken ct) => Task.FromResult(_result);
    }

    [Fact]
    public void DuplicateToolName_Throws()
        => Assert.Throws<InvalidOperationException>(() => new McpToolRouter(new[] { Describe(), Describe() }));

    [Fact]
    public async Task UnknownTool_FailsUnknownTarget_AndAudits()
    {
        var audit = new CapturingAuditSink();
        var router = new McpToolRouter(new[] { Describe() }, audit);

        var result = await router.DispatchAsync("nope", JsonValue.Null, McpToolContext.ForExternal("c", Guid.Empty), default);

        Assert.IsType<UnknownTarget>(Assert.IsType<FailedResult>(result).Reason);
        Assert.Equal(AuditOutcome.Unknown, Assert.Single(audit.Entries).Outcome);
    }

    [Fact]
    public async Task ExternalCaller_CannotReachUserOnlyTool()
    {
        var audit = new CapturingAuditSink();
        var router = new McpToolRouter(new IMcpTool[] { Describe(), new UserOnlyTool() }, audit);

        var result = await router.DispatchAsync("user_only", JsonValue.Null, McpToolContext.ForExternal("c", Guid.Empty), default);

        Assert.IsType<UnknownTarget>(Assert.IsType<FailedResult>(result).Reason); // hidden, not "forbidden"
        Assert.Equal(AuditOutcome.Rejected, Assert.Single(audit.Entries).Outcome);
    }

    [Fact]
    public async Task UserCaller_CanReachUserOnlyTool()
    {
        var router = new McpToolRouter(new IMcpTool[] { new UserOnlyTool() });
        var result = await router.DispatchAsync("user_only", JsonValue.Null, McpToolContext.ForUser(Guid.Empty), default);
        Assert.IsType<DataResult>(result);
    }

    [Fact]
    public async Task Dispatch_Success_Audits()
    {
        var audit = new CapturingAuditSink();
        var router = new McpToolRouter(new[] { Describe() }, audit);

        var result = await router.DispatchAsync("describe_app", JsonValue.Null, McpToolContext.ForExternal("Claude/1.0", Guid.Empty), default);

        Assert.IsType<DataResult>(result);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal("describe_app", entry.ToolName);
        Assert.Equal(AuditOutcome.Success, entry.Outcome);
        Assert.Equal("Claude/1.0", entry.OriginLabel);
        Assert.NotEmpty(entry.PayloadHash); // hashed, not raw args
    }

    [Fact]
    public void ExternalManifest_OnlyAgentSafe_Sorted()
    {
        var router = new McpToolRouter(new IMcpTool[] { new UserOnlyTool(), Describe(), new GetAuthStateTool(() => new AuthSnapshot(false, null)) });
        var names = router.ExternalManifest.Select(d => d.Name).ToList();

        Assert.Equal(new[] { "describe_app", "get_auth_state" }, names); // user_only excluded, sorted
    }

    // ── Agent-activity callback (drives "app follows the agent" navigation) ──────────────────

    [Fact]
    public async Task ExternalRead_FiresAgentActivity_WithToolName()
    {
        string? captured = null;
        var router = new McpToolRouter(
            new IMcpTool[] { new FakeReadTool("list_sessions", ToolResult.Ok(new byte[] { (byte)'1' })) },
            onAgentActivity: name => { captured = name; return Task.CompletedTask; });

        await router.DispatchAsync("list_sessions", JsonValue.Null, McpToolContext.ForExternal("Claude/1.0", Guid.Empty), default);

        Assert.Equal("list_sessions", captured);
    }

    [Fact]
    public async Task InternalCaller_DoesNotFireAgentActivity()
    {
        var fired = false;
        var router = new McpToolRouter(
            new IMcpTool[] { new FakeReadTool("list_sessions", ToolResult.Ok(new byte[] { (byte)'1' })) },
            onAgentActivity: _ => { fired = true; return Task.CompletedTask; });

        await router.DispatchAsync("list_sessions", JsonValue.Null, McpToolContext.ForUser(Guid.Empty), default);

        Assert.False(fired); // only agent (external) calls move the user's view
    }

    [Fact]
    public async Task FailedRead_DoesNotFireAgentActivity()
    {
        var fired = false;
        var router = new McpToolRouter(
            new IMcpTool[] { new FakeReadTool("list_sessions", ToolResult.Fail(new BackendError("boom"))) },
            onAgentActivity: _ => { fired = true; return Task.CompletedTask; });

        await router.DispatchAsync("list_sessions", JsonValue.Null, McpToolContext.ForExternal("c", Guid.Empty), default);

        Assert.False(fired); // don't navigate on a failed read
    }
}
