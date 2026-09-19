using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Proposals;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Mcp.Wire;
using CanfarDesktop.Models;

namespace CanfarDesktop.Tests.Mcp;

/// <summary>
/// The recent-search history write tools: Destructive proposals keyed by (searchedAt, summary) rather
/// than the volatile list index, plus their appliers.
/// </summary>
public class RecentSearchWriteToolsTests
{
    private static JsonValue Args(string json) => JsonValue.Parse(json);

    private static (McpToolContext ctx, InMemoryProposalStore store) Context()
    {
        var store = new InMemoryProposalStore();
        return (McpToolContext.ForExternal("c1", Guid.NewGuid(), store, new ProposalBudget()), store);
    }

    private static readonly List<RecentSearch> History =
    [
        new() { Summary = "M31, CFHT", Adql = "SELECT 1", ResultCount = 12, SearchedAt = new DateTime(2026, 7, 19, 10, 0, 0, DateTimeKind.Utc) },
        new() { Summary = "NGC 1275", Adql = "SELECT 2", ResultCount = 3, SearchedAt = new DateTime(2026, 7, 18, 9, 0, 0, DateTimeKind.Utc) },
    ];

    // ── remove_recent_search ──────────────────────────────────────────────────

    [Fact]
    public async Task RemoveRecentSearch_ResolvesIndexToStableKey()
    {
        var (ctx, _) = Context();
        var tool = new RemoveRecentSearchTool(() => History);

        var result = await tool.InvokeAsync(Args("""{"index":1}"""), ctx, default);

        var proposal = Assert.IsType<ProposedResult>(result).Proposal;
        Assert.Equal("remove_recent_search", proposal.Kind);
        Assert.Equal("Remove recent search: NGC 1275", proposal.Summary);
        var payload = JsonSerializer.Deserialize<RemoveRecentSearchPayload>(proposal.Payload, McpJson.Options)!;
        Assert.Equal(History[1].SearchedAt, payload.SearchedAt);
        Assert.Equal("NGC 1275", payload.Summary);
    }

    [Fact]
    public async Task RemoveRecentSearch_IndexOutOfRange_UnknownTarget()
    {
        var (ctx, store) = Context();
        var tool = new RemoveRecentSearchTool(() => History);
        var result = await tool.InvokeAsync(Args("""{"index":5}"""), ctx, default);
        Assert.IsType<UnknownTarget>(Assert.IsType<FailedResult>(result).Reason);
        Assert.Empty(store.List());
    }

    [Fact]
    public async Task RemoveRecentSearch_MissingIndex_InvalidArgument()
    {
        var (ctx, _) = Context();
        var tool = new RemoveRecentSearchTool(() => History);
        var result = await tool.InvokeAsync(Args("{}"), ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
    }

    // ── clear_recent_searches ─────────────────────────────────────────────────

    [Fact]
    public async Task ClearRecentSearches_BuildsProposalWithCount()
    {
        var (ctx, _) = Context();
        var tool = new ClearRecentSearchesTool(() => History);
        var result = await tool.InvokeAsync(Args("{}"), ctx, default);
        var proposal = Assert.IsType<ProposedResult>(result).Proposal;
        Assert.Equal("clear_recent_searches", proposal.Kind);
        Assert.Equal("Clear all 2 recent searches", proposal.Summary);
    }

    [Fact]
    public void BothTools_AreDestructive()
    {
        Assert.Equal(McpVerbClass.Destructive, new RemoveRecentSearchTool(() => History).VerbClass);
        Assert.Equal(McpVerbClass.Destructive, new ClearRecentSearchesTool(() => History).VerbClass);
    }

    // ── appliers ──────────────────────────────────────────────────────────────

    private static PendingProposal ProposalWith<T>(string kind, T payload)
        => PendingProposal.Create("tool", kind, "summary",
            JsonSerializer.SerializeToUtf8Bytes(payload, McpJson.Options), OperationOrigin.External("c1"));

    [Fact]
    public async Task RemoveRecentSearchApplier_DecodesAndInvokes()
    {
        RemoveRecentSearchPayload? applied = null;
        var applier = new RemoveRecentSearchApplier(p => { applied = p; return Task.CompletedTask; });

        Assert.Equal("remove_recent_search", applier.Kind);
        await applier.ApplyAsync(ProposalWith("remove_recent_search",
            new RemoveRecentSearchPayload(History[0].SearchedAt, "M31, CFHT")));

        Assert.NotNull(applied);
        Assert.Equal(History[0].SearchedAt, applied!.SearchedAt);
        Assert.Equal("M31, CFHT", applied.Summary);
    }

    [Fact]
    public async Task ClearRecentSearchesApplier_Invokes()
    {
        var cleared = false;
        var applier = new ClearRecentSearchesApplier(() => { cleared = true; return Task.CompletedTask; });
        Assert.Equal("clear_recent_searches", applier.Kind);
        await applier.ApplyAsync(ProposalWith("clear_recent_searches", new ClearRecentSearchesPayload(2)));
        Assert.True(cleared);
    }
}
