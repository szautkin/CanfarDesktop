using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Proposals;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

public class SessionWriteToolsTests
{
    private static JsonValue Args(string json) => JsonValue.Parse(json);

    private static (McpToolContext ctx, InMemoryProposalStore store) Context()
    {
        var store = new InMemoryProposalStore();
        return (McpToolContext.ForExternal("c1", Guid.NewGuid(), store, new ProposalBudget()), store);
    }

    // ── launch_session ────────────────────────────────────────────────────────

    [Fact]
    public async Task LaunchSession_BuildsProposal()
    {
        var (ctx, _) = Context();
        var result = await new LaunchSessionTool().InvokeAsync(
            Args("""{"type":"notebook","image":"images.canfar.net/skaha/astroml:1.0","cores":4,"ram":16}"""), ctx, default);

        var proposal = Assert.IsType<ProposedResult>(result).Proposal;
        Assert.Equal("launch_session", proposal.Kind);
        var payload = JsonSerializer.Deserialize<LaunchSessionPayload>(proposal.Payload, McpJson.Options)!;
        Assert.Equal("notebook", payload.Type);
        Assert.Equal(4, payload.Cores);
    }

    [Theory]
    [InlineData("""{"type":"headless","image":"x"}""")]   // headless not allowed here
    [InlineData("""{"type":"notebook"}""")]                // image missing
    [InlineData("""{"type":"notebook","image":"x","cores":0}""")] // bad cores
    public async Task LaunchSession_Invalid_InvalidArgument(string json)
    {
        var (ctx, store) = Context();
        var result = await new LaunchSessionTool().InvokeAsync(Args(json), ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
        Assert.Empty(store.List());
    }

    [Fact]
    public void VerbClasses_AreCorrect()
    {
        Assert.Equal(McpVerbClass.SemanticWrite, new LaunchSessionTool().VerbClass);
        Assert.Equal(McpVerbClass.SemanticWrite, new LaunchHeadlessJobTool().VerbClass);
        Assert.Equal(McpVerbClass.Destructive, new DeleteSessionTool().VerbClass);
        Assert.Equal(McpVerbClass.SemanticWrite, new RenewSessionTool().VerbClass);
    }

    // ── launch_headless_job ───────────────────────────────────────────────────

    [Fact]
    public async Task LaunchHeadless_BuildsProposal_WithReplicas()
    {
        var (ctx, _) = Context();
        var result = await new LaunchHeadlessJobTool().InvokeAsync(
            Args("""{"image":"img","args":"python run.py","replicas":3}"""), ctx, default);
        var payload = JsonSerializer.Deserialize<LaunchHeadlessPayload>(Assert.IsType<ProposedResult>(result).Proposal.Payload, McpJson.Options)!;
        Assert.Equal(3, payload.Replicas);
        Assert.Equal("python run.py", payload.Args);
    }

    [Fact]
    public async Task LaunchHeadless_TooManyReplicas_InvalidArgument()
    {
        var (ctx, _) = Context();
        var result = await new LaunchHeadlessJobTool().InvokeAsync(Args("""{"image":"x","replicas":51}"""), ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
    }

    // ── delete / renew ────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAndRenew_BuildProposals()
    {
        var (ctx, _) = Context();
        var del = await new DeleteSessionTool().InvokeAsync(Args("""{"id":"abc"}"""), ctx, default);
        Assert.Equal("delete_session", Assert.IsType<ProposedResult>(del).Proposal.Kind);

        var renew = await new RenewSessionTool().InvokeAsync(Args("""{"id":"abc"}"""), ctx, default);
        Assert.Equal("renew_session", Assert.IsType<ProposedResult>(renew).Proposal.Kind);
    }

    // ── appliers decode + invoke ──────────────────────────────────────────────

    private static PendingProposal Proposal<T>(string kind, T payload)
        => PendingProposal.Create("t", kind, "s", JsonSerializer.SerializeToUtf8Bytes(payload, McpJson.Options), OperationOrigin.External("c1"));

    [Fact]
    public async Task LaunchSessionApplier_DecodesAndInvokes()
    {
        LaunchSessionPayload? seen = null;
        var applier = new LaunchSessionApplier(p => { seen = p; return Task.CompletedTask; });
        Assert.Equal("launch_session", applier.Kind);
        await applier.ApplyAsync(Proposal("launch_session", new LaunchSessionPayload("desktop", "img", "n", 2, 8, 0)));
        Assert.Equal("desktop", seen!.Type);
    }

    [Fact]
    public async Task DeleteSessionApplier_DecodesAndInvokes()
    {
        string? id = null;
        var applier = new DeleteSessionApplier(p => { id = p.Id; return Task.CompletedTask; });
        await applier.ApplyAsync(Proposal("delete_session", new DeleteSessionPayload("sess-1")));
        Assert.Equal("sess-1", id);
    }

    // ── delete_sessions_bulk ──────────────────────────────────────────────────

    [Fact]
    public async Task DeleteSessionsBulk_BuildsDestructiveProposal_TrimmedDedupedIds()
    {
        var (ctx, _) = Context();
        var tool = new DeleteSessionsBulkTool();
        Assert.Equal(McpVerbClass.Destructive, tool.VerbClass);

        var result = await tool.InvokeAsync(
            Args("""{"ids":[" s1 ","s2","s1","   ","s3"]}"""), ctx, default);

        var proposal = Assert.IsType<ProposedResult>(result).Proposal;
        Assert.Equal("delete_sessions_bulk", proposal.Kind);
        Assert.Equal("Terminate 3 sessions", proposal.Summary);
        var payload = JsonSerializer.Deserialize<DeleteSessionsBulkPayload>(proposal.Payload, McpJson.Options)!;
        Assert.Equal(new[] { "s1", "s2", "s3" }, payload.Ids);
    }

    [Fact]
    public async Task DeleteSessionsBulk_SingleId_SingularSummary()
    {
        var (ctx, _) = Context();
        var result = await new DeleteSessionsBulkTool().InvokeAsync(Args("""{"ids":["s1"]}"""), ctx, default);
        Assert.Equal("Terminate 1 session", Assert.IsType<ProposedResult>(result).Proposal.Summary);
    }

    [Theory]
    [InlineData("""{"ids":[]}""")]
    [InlineData("""{}""")]
    [InlineData("""{"ids":["  ","  "]}""")] // blanks only → empty after cleaning
    public async Task DeleteSessionsBulk_EmptyIds_InvalidArgument(string argsJson)
    {
        var (ctx, store) = Context();
        var result = await new DeleteSessionsBulkTool().InvokeAsync(Args(argsJson), ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
        Assert.Empty(store.List());
    }

    [Fact]
    public async Task DeleteSessionsBulk_OverFiftyUniqueIds_InvalidArgument()
    {
        var (ctx, store) = Context();
        var ids = string.Join(",", Enumerable.Range(0, 51).Select(i => $"\"s{i}\""));
        var result = await new DeleteSessionsBulkTool().InvokeAsync(Args($$"""{"ids":[{{ids}}]}"""), ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
        Assert.Empty(store.List());
    }

    [Fact]
    public async Task DeleteSessionsBulkApplier_AttemptsEveryId_EvenWhenSomeFail()
    {
        var attempted = new List<string>();
        var applier = new DeleteSessionsBulkApplier(id =>
        {
            attempted.Add(id);
            return id == "s2" ? throw new HttpRequestException("already gone") : Task.CompletedTask;
        });
        Assert.Equal("delete_sessions_bulk", applier.Kind);

        // Partial-success semantics: the already-gone s2 must not block s3.
        await applier.ApplyAsync(Proposal("delete_sessions_bulk", new DeleteSessionsBulkPayload(new[] { "s1", "s2", "s3" })));

        Assert.Equal(new[] { "s1", "s2", "s3" }, attempted);
    }
}
