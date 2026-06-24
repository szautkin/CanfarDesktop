using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Proposals;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

public class WritePathTests
{
    private static JsonValue Args(string json) => JsonValue.Parse(json);
    private static readonly OperationOrigin Client = OperationOrigin.External("c1");

    private static (McpToolContext ctx, InMemoryProposalStore store, ProposalBudget budget) Context(int limit = 8)
    {
        var store = new InMemoryProposalStore();
        var budget = new ProposalBudget(limit);
        return (McpToolContext.ForExternal("c1", Guid.NewGuid(), store, budget), store, budget);
    }

    private sealed class FakeWriteTool : JsonWriteTool<FakeWriteTool.Args>
    {
        private readonly McpVerbClass _verb;
        public FakeWriteTool(McpVerbClass verb) => _verb = verb;
        public override McpVerbClass VerbClass => _verb;
        public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
            "fake_write", "fake",
            """{"type":"object","properties":{"name":{"type":"string"}},"required":["name"],"additionalProperties":false}""");

        protected override Task<ProposalPlan> PlanAsync(Args args, McpToolContext context, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(args.Name)) throw new McpToolException(new InvalidArgument("name required"));
            return Task.FromResult(ProposalPlan.Encoding("fake_write", $"do {args.Name}", new { args.Name }));
        }

        public sealed record Args { public string Name { get; init; } = string.Empty; }
    }

    // ── JsonWriteTool base ────────────────────────────────────────────────────

    [Fact]
    public async Task WriteTool_EnqueuesProposal()
    {
        var (ctx, store, _) = Context();
        var result = await new FakeWriteTool(McpVerbClass.SemanticWrite).InvokeAsync(Args("""{"name":"x"}"""), ctx, default);

        var proposed = Assert.IsType<ProposedResult>(result);
        Assert.Equal("fake_write", proposed.Proposal.Kind);
        Assert.Single(store.List());
    }

    [Fact]
    public async Task WriteTool_NoStoreInContext_BackendError()
    {
        var ctx = McpToolContext.ForExternal("c1", Guid.NewGuid()); // no proposals
        var result = await new FakeWriteTool(McpVerbClass.SemanticWrite).InvokeAsync(Args("""{"name":"x"}"""), ctx, default);
        Assert.IsType<BackendError>(Assert.IsType<FailedResult>(result).Reason);
    }

    [Fact]
    public async Task WriteTool_PlanValidationFails_NotEnqueued()
    {
        var (ctx, store, _) = Context();
        var result = await new FakeWriteTool(McpVerbClass.SemanticWrite).InvokeAsync(Args("""{"name":""}"""), ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
        Assert.Empty(store.List());
    }

    // ── Router write path ─────────────────────────────────────────────────────

    [Fact]
    public async Task Router_Queue_ConsumesBudget()
    {
        var (ctx, store, budget) = Context();
        var router = new McpToolRouter(new[] { new FakeWriteTool(McpVerbClass.SemanticWrite) });

        var result = await router.DispatchAsync("fake_write", Args("""{"name":"x"}"""), ctx, default);

        Assert.IsType<ProposedResult>(result);
        Assert.Single(store.List());
        Assert.Equal(7, budget.Remaining(Client));
    }

    [Fact]
    public async Task Router_AutoApply_AppliesAndReturnsAck()
    {
        var (ctx, store, budget) = Context();
        var applied = new List<Guid>();
        var hook = new AutoApplyHook(
            (verb, proposal) => Task.FromResult(true),
            id => { applied.Add(id); store.MarkApplied(id); return Task.CompletedTask; });
        var router = new McpToolRouter(new[] { new FakeWriteTool(McpVerbClass.SemanticWrite) }, autoApplyHook: hook);

        var result = await router.DispatchAsync("fake_write", Args("""{"name":"x"}"""), ctx, default);

        var data = Assert.IsType<DataResult>(result);
        var doc = JsonDocument.Parse(data.Json).RootElement;
        Assert.True(doc.GetProperty("applied").GetBoolean());
        Assert.Equal("fake_write", doc.GetProperty("kind").GetString());
        Assert.Single(applied);
        Assert.Equal(8, budget.Remaining(Client)); // auto-apply bypasses the budget
    }

    [Fact]
    public async Task Router_AutoApplyFailure_LeavesProposalQueued()
    {
        var (ctx, store, _) = Context();
        var hook = new AutoApplyHook(
            (verb, proposal) => Task.FromResult(true),
            id => throw new InvalidOperationException("backend down"));
        var router = new McpToolRouter(new[] { new FakeWriteTool(McpVerbClass.SemanticWrite) }, autoApplyHook: hook);

        var result = await router.DispatchAsync("fake_write", Args("""{"name":"x"}"""), ctx, default);

        Assert.IsType<BackendError>(Assert.IsType<FailedResult>(result).Reason);
        Assert.Single(store.List()); // still queued for retry/reject
    }

    [Fact]
    public async Task Router_BudgetExceeded_WithdrawsAndFails()
    {
        var (ctx, store, _) = Context(limit: 1);
        var router = new McpToolRouter(new[] { new FakeWriteTool(McpVerbClass.SemanticWrite) });

        var first = await router.DispatchAsync("fake_write", Args("""{"name":"a"}"""), ctx, default);
        var second = await router.DispatchAsync("fake_write", Args("""{"name":"b"}"""), ctx, default);

        Assert.IsType<ProposedResult>(first);
        Assert.IsType<PerTurnProposalCapExceeded>(Assert.IsType<FailedResult>(second).Reason);
        Assert.Single(store.List()); // the rejected proposal was withdrawn
    }
}
