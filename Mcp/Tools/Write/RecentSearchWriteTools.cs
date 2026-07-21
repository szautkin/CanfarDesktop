using CanfarDesktop.Mcp.Tools.Proposals;
using CanfarDesktop.Models;

namespace CanfarDesktop.Mcp.Tools.Write;

// ─────────────────────────────────────────────────────────────────────────────
// Recent-search history writes (the Recent Searches side panel's remove / Clear
// All buttons). Both delete user history, so they are Destructive proposals —
// they always queue for explicit approval, never auto-apply. The remove tool
// resolves the agent-visible index to a stable (searchedAt, summary) key at plan
// time, so the proposal still deletes the intended entry even if the list shifts
// before the user approves it.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Proposal payload for <c>remove_recent_search</c> — keyed by timestamp + summary, not index.</summary>
public sealed record RemoveRecentSearchPayload(DateTime SearchedAt, string Summary);

/// <summary>Proposal payload for <c>clear_recent_searches</c> (count is for the summary only).</summary>
public sealed record ClearRecentSearchesPayload(int Count);

/// <summary><c>remove_recent_search</c> — propose removing one entry from the search history. Destructive.</summary>
public sealed class RemoveRecentSearchTool : JsonWriteTool<RemoveRecentSearchTool.Args>
{
    private readonly Func<IReadOnlyList<RecentSearch>> _recent;

    public RemoveRecentSearchTool(Func<IReadOnlyList<RecentSearch>> recent) => _recent = recent;

    public override McpVerbClass VerbClass => McpVerbClass.Destructive;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "remove_recent_search",
        "Propose removing one entry from the user's recent-search history. `index` is 0-based, newest " +
        "first, matching list_recent_searches order. Queues for the user to apply (a destructive change).",
        """{"type":"object","properties":{"index":{"type":"integer","minimum":0}},"required":["index"],"additionalProperties":false}""");

    protected override Task<ProposalPlan> PlanAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (args.Index is null or < 0)
            throw new McpToolException(new InvalidArgument("index (>= 0) is required"));

        var recent = _recent();
        if (args.Index.Value >= recent.Count)
            throw new McpToolException(new UnknownTarget(
                $"no recent search at index {args.Index.Value} (history has {recent.Count} entries)"));

        var entry = recent[args.Index.Value];
        return Task.FromResult(ProposalPlan.Encoding(
            "remove_recent_search",
            $"Remove recent search: {entry.Summary}",
            new RemoveRecentSearchPayload(entry.SearchedAt, entry.Summary)));
    }

    public sealed record Args { public int? Index { get; init; } }
}

/// <summary><c>clear_recent_searches</c> — propose clearing the whole search history. Destructive.</summary>
public sealed class ClearRecentSearchesTool : JsonWriteTool<EmptyArgs>
{
    private readonly Func<IReadOnlyList<RecentSearch>> _recent;

    public ClearRecentSearchesTool(Func<IReadOnlyList<RecentSearch>> recent) => _recent = recent;

    public override McpVerbClass VerbClass => McpVerbClass.Destructive;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "clear_recent_searches",
        "Propose clearing the user's entire recent-search history (the panel's Clear All button). Queues " +
        "for the user to apply (a destructive change).",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override Task<ProposalPlan> PlanAsync(EmptyArgs args, McpToolContext context, CancellationToken ct)
    {
        var count = _recent().Count;
        return Task.FromResult(ProposalPlan.Encoding(
            "clear_recent_searches",
            $"Clear all {count} recent searches",
            new ClearRecentSearchesPayload(count)));
    }
}

/// <summary>Applies a <c>remove_recent_search</c> proposal via the injected store action.</summary>
public sealed class RemoveRecentSearchApplier : IProposalApplier
{
    private readonly Func<RemoveRecentSearchPayload, Task> _remove;
    public RemoveRecentSearchApplier(Func<RemoveRecentSearchPayload, Task> remove) => _remove = remove;

    public string Kind => "remove_recent_search";

    public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
        => _remove(ProposalPayload.Decode<RemoveRecentSearchPayload>(proposal));
}

/// <summary>Applies a <c>clear_recent_searches</c> proposal via the injected store action.</summary>
public sealed class ClearRecentSearchesApplier : IProposalApplier
{
    private readonly Func<Task> _clear;
    public ClearRecentSearchesApplier(Func<Task> clear) => _clear = clear;

    public string Kind => "clear_recent_searches";

    public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
        => _clear();
}
