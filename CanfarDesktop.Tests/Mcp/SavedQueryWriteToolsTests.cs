using System.Text;
using System.Text.Json;
using Xunit;
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
}
