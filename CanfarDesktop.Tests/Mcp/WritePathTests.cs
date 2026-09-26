using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Proposals;
using CanfarDesktop.Mcp.Wire;
using CanfarDesktop.Services.AiGuide;
using CanfarDesktop.Tests.Helpers;

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

        public FakeWriteTool(McpVerbClass verb, string name = "fake_write")
        {
            _verb = verb;
            Descriptor = ToolDescriptor.WithStaticSchema(
                name, "fake",
                """{"type":"object","properties":{"name":{"type":"string"}},"required":["name"],"additionalProperties":false}""");
        }

        public override McpVerbClass VerbClass => _verb;
        public override ToolDescriptor Descriptor { get; }

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
    public async Task Router_AutoApplyFailure_WithdrawsProposal()
    {
        // QA 1.3.0.0 criterion #4: an auto-apply that already failed at the backend must NOT leave a
        // doomed proposal in the queue — applying it from the strip can only reproduce the failure.
        // The error is returned to the agent; the proposal is withdrawn.
        var (ctx, store, _) = Context();
        var hook = new AutoApplyHook(
            (verb, proposal) => Task.FromResult(true),
            id => throw new InvalidOperationException("backend down"));
        var router = new McpToolRouter(new[] { new FakeWriteTool(McpVerbClass.SemanticWrite) }, autoApplyHook: hook);

        var result = await router.DispatchAsync("fake_write", Args("""{"name":"x"}"""), ctx, default);

        Assert.IsType<BackendError>(Assert.IsType<FailedResult>(result).Reason);
        Assert.Empty(store.List()); // withdrawn — no doomed item left behind
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

    // ── When a change applies, as the agent is told (QA D9) ───────────────────

    /// <summary>The host's hook, deciding by the real policy.</summary>
    private static AutoApplyHook PolicyHook(bool autoApplyOn, InMemoryProposalStore store, List<Guid> applied) => new(
        (verb, _) => Task.FromResult(AutoApplyPolicy.ShouldAutoApply(autoApplyOn, verb)),
        id => { applied.Add(id); store.MarkApplied(id); return Task.CompletedTask; });

    [Theory]
    [InlineData(McpVerbClass.SemanticWrite, true, true)]
    [InlineData(McpVerbClass.SemanticWrite, false, false)]
    [InlineData(McpVerbClass.Destructive, true, false)] // D9: it was applied at once
    [InlineData(McpVerbClass.Destructive, false, false)]
    public async Task Router_AppliesAtOnce_OnlyWhatThePolicyLets(McpVerbClass verb, bool autoApplyOn, bool appliesAtOnce)
    {
        var (ctx, store, _) = Context();
        var applied = new List<Guid>();
        var router = new McpToolRouter(new[] { new FakeWriteTool(verb) }, autoApplyHook: PolicyHook(autoApplyOn, store, applied));

        var result = await router.DispatchAsync("fake_write", Args("""{"name":"x"}"""), ctx, default);

        if (appliesAtOnce)
        {
            Assert.IsType<DataResult>(result);
            Assert.Single(applied);
        }
        else
        {
            Assert.IsType<ProposedResult>(result);
            Assert.Empty(applied);
            Assert.Single(store.List()); // waiting in Pending
        }
    }

    /// <summary>A destructive tool that acts at once rather than proposing, as removing a mark does.</summary>
    private sealed class ActsAtOnce : IMcpTool
    {
        public McpVerbClass VerbClass => McpVerbClass.Destructive;
        public bool AgentSafe => true;
        public ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema("acts_at_once", "gone.", """{"type":"object"}""");

        public Task<ToolResult> InvokeAsync(JsonValue arguments, McpToolContext context, CancellationToken cancellationToken)
            => Task.FromResult(ToolResult.Ok("{}"u8.ToArray()));
    }

    [Fact]
    public void Manifest_SaysWhenEachToolsChangesApply()
    {
        var write = new FakeWriteTool(McpVerbClass.SemanticWrite, "write");
        var router = new McpToolRouter(new IMcpTool[]
        {
            write,
            new FakeWriteTool(McpVerbClass.Destructive, "delete"),
            new ActsAtOnce(),
            new AliasedTool("write_alias", "fake", write),
            new SignedInTool(new FakeWriteTool(McpVerbClass.SemanticWrite, "signed_write"), () => false, "sign in"),
        });
        string DescriptionOf(string name) => router.ExternalManifest.Single(d => d.Name == name).Description;

        Assert.Equal($"fake {AutoApplyPolicy.Note(McpVerbClass.SemanticWrite)}", DescriptionOf("write"));
        Assert.Equal($"fake {AutoApplyPolicy.Note(McpVerbClass.Destructive)}", DescriptionOf("delete"));
        Assert.Equal("gone.", DescriptionOf("acts_at_once")); // it does not wait for anyone, so it does not say so
        Assert.Equal(DescriptionOf("write"), DescriptionOf("write_alias"));
        Assert.Equal(DescriptionOf("write"), DescriptionOf("signed_write"));
    }

    [Fact]
    public void Note_OnlyForChanges()
    {
        Assert.Null(AutoApplyPolicy.Note(McpVerbClass.Read));
        Assert.Contains("\"Auto-apply agent writes\" on", AutoApplyPolicy.Note(McpVerbClass.SemanticWrite));
        Assert.Contains("always waits", AutoApplyPolicy.Note(McpVerbClass.Destructive));
    }

    [Fact]
    public async Task ToolsList_KeepsTheNote_UnderTheDescriptionThePersonWrote()
    {
        var router = new McpToolRouter(new IMcpTool[] { new FakeWriteTool(McpVerbClass.Destructive, "delete") });
        var guide = new AiGuideSnapshot(new Dictionary<string, string> { ["delete"] = "Mine." }, []);
        var server = new McpServerService(router, new ServerIdentity("verbinal-canfar", "1"), aiGuide: () => guide);

        await server.HandleFrameAsync(System.Text.Encoding.UTF8.GetBytes(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","clientInfo":{"name":"t","version":"1"}}}"""));
        var answer = await server.HandleFrameAsync(System.Text.Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}"""));

        var tool = ((JsonArray)JsonValue.Parse(System.Text.Encoding.UTF8.GetString(answer!))["result"]!["tools"]!).Items.Single();
        Assert.Equal($"Mine. {AutoApplyPolicy.Note(McpVerbClass.Destructive)}", ((JsonString)tool["description"]!).Value);
    }

    /// <summary>
    /// No tool says in its own words when its change applies: that is the policy's to say, and tools
    /// saying it by hand said it wrong (QA D9) — "queues for the user to apply" with auto-apply on,
    /// "unless auto-apply is on" for a change auto-apply never touches.
    /// </summary>
    [Fact]
    public void NoToolDescription_SaysForItselfWhenItsChangeApplies()
    {
        var claim = new System.Text.RegularExpressions.Regex(
            @"Queues for the user|[Aa]uto-applies|[Aa]lways waits for the user|queues for (their |the user's )?approval|unless auto-apply");
        var tools = RepoFiles.PathTo(Path.Combine("Mcp", "Tools")) + Path.DirectorySeparatorChar;

        var offenders = McpToolSources.AppSources()
            .Where(f => f.StartsWith(tools, StringComparison.OrdinalIgnoreCase))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (f, i, line)))
            .Where(x => !x.line.TrimStart().StartsWith("//") && claim.IsMatch(x.line))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}")
            .ToList();

        Assert.True(offenders.Count == 0, "Say it with AutoApplyPolicy.Note instead: " + string.Join(", ", offenders));
    }
}
