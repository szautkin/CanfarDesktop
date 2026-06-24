using System.Text;
using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Proposals;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

public class ProposalLifecycleToolsTests
{
    private static JsonValue Args(string json) => JsonValue.Parse(json);

    private static (McpToolContext ctx, InMemoryProposalStore store) Context()
    {
        var store = new InMemoryProposalStore();
        return (McpToolContext.ForExternal("c1", Guid.NewGuid(), store, new ProposalBudget()), store);
    }

    private static PendingProposal Enqueue(InMemoryProposalStore store, string kind)
        => store.Enqueue(PendingProposal.Create("tool", kind, $"do {kind}", Encoding.UTF8.GetBytes("{}"), OperationOrigin.External("c1")));

    private static JsonElement Json(ToolResult result)
        => JsonDocument.Parse(Assert.IsType<DataResult>(result).Json).RootElement;

    [Fact]
    public async Task ListPendingProposals_ReturnsQueued()
    {
        var (ctx, store) = Context();
        Enqueue(store, "save_query");
        Enqueue(store, "delete_saved_query");

        var doc = Json(await new ListPendingProposalsTool().InvokeAsync(JsonValue.Null, ctx, default));

        Assert.Equal(2, doc.GetProperty("count").GetInt32());
        Assert.Equal("save_query", doc.GetProperty("proposals")[0].GetProperty("kind").GetString());
    }

    [Fact]
    public void ListPendingProposals_VerbClassIsLifecycle()
        => Assert.Equal(McpVerbClass.ProposalLifecycle, new ListPendingProposalsTool().VerbClass);

    [Fact]
    public async Task GetProposalState_ReflectsLifecycle()
    {
        var (ctx, store) = Context();
        var p = Enqueue(store, "save_query");

        var pending = Json(await new GetProposalStateTool().InvokeAsync(Args($$"""{"id":"{{p.Id}}"}"""), ctx, default));
        Assert.Equal("pending", pending.GetProperty("state").GetString());

        store.MarkApplied(p.Id);
        var applied = Json(await new GetProposalStateTool().InvokeAsync(Args($$"""{"id":"{{p.Id}}"}"""), ctx, default));
        Assert.Equal("applied", applied.GetProperty("state").GetString());
    }

    [Fact]
    public async Task GetProposalState_BadId_InvalidArgument()
    {
        var (ctx, _) = Context();
        var result = await new GetProposalStateTool().InvokeAsync(Args("""{"id":"not-a-guid"}"""), ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
    }

    [Fact]
    public async Task WithdrawProposal_RemovesFromQueue()
    {
        var (ctx, store) = Context();
        var p = Enqueue(store, "save_query");

        var doc = Json(await new WithdrawProposalTool().InvokeAsync(Args($$"""{"id":"{{p.Id}}"}"""), ctx, default));
        Assert.True(doc.GetProperty("withdrew").GetBoolean());
        Assert.Empty(store.List());

        var again = Json(await new WithdrawProposalTool().InvokeAsync(Args($$"""{"id":"{{p.Id}}"}"""), ctx, default));
        Assert.False(again.GetProperty("withdrew").GetBoolean()); // already resolved
    }
}
