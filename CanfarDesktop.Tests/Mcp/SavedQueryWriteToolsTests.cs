using System.Text;
using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Proposals;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

public class SavedQueryWriteToolsTests
{
    private static JsonValue Args(string json) => JsonValue.Parse(json);

    private static (McpToolContext ctx, InMemoryProposalStore store) Context()
    {
        var store = new InMemoryProposalStore();
        return (McpToolContext.ForExternal("c1", Guid.NewGuid(), store, new ProposalBudget()), store);
    }

    // ── save_query tool ───────────────────────────────────────────────────────

    [Fact]
    public async Task SaveQuery_BuildsProposalWithPayload()
    {
        var (ctx, store) = Context();
        var result = await new SaveQueryTool().InvokeAsync(Args("""{"name":"My cone","adql":"SELECT 1"}"""), ctx, default);

        var proposal = Assert.IsType<ProposedResult>(result).Proposal;
        Assert.Equal("save_query", proposal.Kind);
        Assert.Equal("Save query: My cone", proposal.Summary);

        var payload = JsonSerializer.Deserialize<SaveQueryPayload>(proposal.Payload, McpJson.Options)!;
        Assert.Equal("My cone", payload.Name);
        Assert.Equal("SELECT 1", payload.Adql);
        Assert.Single(store.List());
    }

    [Theory]
    [InlineData("""{"name":"","adql":"SELECT 1"}""")]
    [InlineData("""{"name":"x","adql":"  "}""")]
    public async Task SaveQuery_MissingFields_InvalidArgument_NotQueued(string json)
    {
        var (ctx, store) = Context();
        var result = await new SaveQueryTool().InvokeAsync(Args(json), ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
        Assert.Empty(store.List());
    }

    [Fact]
    public void SaveQuery_IsSemanticWrite_DeleteIsDestructive()
    {
        Assert.Equal(McpVerbClass.SemanticWrite, new SaveQueryTool().VerbClass);
        Assert.Equal(McpVerbClass.Destructive, new DeleteSavedQueryTool().VerbClass);
    }

    // ── delete_saved_query tool ───────────────────────────────────────────────

    [Fact]
    public async Task DeleteSavedQuery_BuildsProposal()
    {
        var (ctx, _) = Context();
        var result = await new DeleteSavedQueryTool().InvokeAsync(Args("""{"name":"My cone"}"""), ctx, default);
        var proposal = Assert.IsType<ProposedResult>(result).Proposal;
        Assert.Equal("delete_saved_query", proposal.Kind);
        var payload = JsonSerializer.Deserialize<DeleteSavedQueryPayload>(proposal.Payload, McpJson.Options)!;
        Assert.Equal("My cone", payload.Name);
    }

    // ── appliers ──────────────────────────────────────────────────────────────

    private static PendingProposal ProposalWith<T>(string kind, T payload)
        => PendingProposal.Create("tool", kind, "summary",
            JsonSerializer.SerializeToUtf8Bytes(payload, McpJson.Options), OperationOrigin.External("c1"));

    [Fact]
    public async Task SaveQueryApplier_DecodesAndInvokes()
    {
        SaveQueryPayload? applied = null;
        var applier = new SaveQueryApplier(p => { applied = p; return Task.CompletedTask; });

        Assert.Equal("save_query", applier.Kind);
        await applier.ApplyAsync(ProposalWith("save_query", new SaveQueryPayload("N", "SELECT 2")));

        Assert.NotNull(applied);
        Assert.Equal("N", applied!.Name);
        Assert.Equal("SELECT 2", applied.Adql);
    }

    [Fact]
    public async Task DeleteSavedQueryApplier_DecodesAndInvokes()
    {
        string? deleted = null;
        var applier = new DeleteSavedQueryApplier(p => { deleted = p.Name; return Task.CompletedTask; });
        await applier.ApplyAsync(ProposalWith("delete_saved_query", new DeleteSavedQueryPayload("Gone")));
        Assert.Equal("Gone", deleted);
    }

    // ── End-to-end: save_query through the router with auto-apply + a real applier ─────────────

    [Fact]
    public async Task SaveQuery_AutoApply_EndToEnd_AppliesAndMarks()
    {
        var store = new InMemoryProposalStore();
        var ctx = McpToolContext.ForExternal("c1", Guid.NewGuid(), store, new ProposalBudget());

        SaveQueryPayload? saved = null;
        var registry = new ProposalApplierRegistry();
        registry.Register(new SaveQueryApplier(p => { saved = p; return Task.CompletedTask; }));

        var hook = new AutoApplyHook(
            (verb, proposal) => Task.FromResult(true),
            async id =>
            {
                var proposal = store.Get(id)!;
                await registry.ApplierFor(proposal.Kind)!.ApplyAsync(proposal);
                store.MarkApplied(id);
            });
        var router = new McpToolRouter(new IMcpTool[] { new SaveQueryTool() }, autoApplyHook: hook);

        var result = await router.DispatchAsync("save_query", Args("""{"name":"N","adql":"SELECT 1"}"""), ctx, default);

        var doc = JsonDocument.Parse(Assert.IsType<DataResult>(result).Json).RootElement;
        Assert.True(doc.GetProperty("applied").GetBoolean());
        Assert.Equal("N", saved!.Name);
        Assert.Equal(ProposalState.Applied, store.State(Guid.Parse(doc.GetProperty("proposalId").GetString()!)));
        Assert.Empty(store.List());
    }
}
