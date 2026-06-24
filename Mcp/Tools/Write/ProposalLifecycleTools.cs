using CanfarDesktop.Mcp.Tools.Proposals;

namespace CanfarDesktop.Mcp.Tools.Write;

/// <summary>
/// <c>list_pending_proposals</c> — the write proposals currently queued for the user to apply or reject.
/// Verb class ProposalLifecycle: manages the queue itself, so it bypasses the proposal gate. 1-to-1 with macOS.
/// </summary>
public sealed class ListPendingProposalsTool : JsonReadTool<EmptyArgs, ListPendingProposalsTool.Output>
{
    public override McpVerbClass VerbClass => McpVerbClass.ProposalLifecycle;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "list_pending_proposals",
        "List the write proposals currently queued for the user to apply or reject — each with its id, " +
        "originating tool, kind, summary, creation time, and origin. Apply/reject happens in the app UI.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override Task<Output> HandleAsync(EmptyArgs args, McpToolContext context, CancellationToken ct)
    {
        var items = (context.Proposals?.List() ?? Array.Empty<PendingProposal>())
            .Select(p => new Item(p.Id.ToString(), p.ToolName, p.Kind, p.Summary, p.CreatedAt.ToString("o"), p.Origin.Label))
            .ToList();
        return Task.FromResult(new Output(items.Count, items));
    }

    public sealed record Item(string Id, string ToolName, string Kind, string Summary, string CreatedAtISO, string OriginTag);
    public sealed record Output(int Count, IReadOnlyList<Item> Proposals);
}

/// <summary>
/// <c>get_proposal_state</c> — poll a proposal's outcome: pending / applied / rejected / withdrawn, or
/// unknown once it ages out of the retention window. Verb class ProposalLifecycle.
/// </summary>
public sealed class GetProposalStateTool : JsonReadTool<GetProposalStateTool.Args, GetProposalStateTool.Output>
{
    public override McpVerbClass VerbClass => McpVerbClass.ProposalLifecycle;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_proposal_state",
        "Get the state of a proposal by id: pending, applied, rejected, withdrawn, or unknown (after it " +
        "ages out of the ~5-minute retention window).",
        """{"type":"object","properties":{"id":{"type":"string"}},"required":["id"],"additionalProperties":false}""");

    protected override Task<Output> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (!Guid.TryParse(args.Id, out var id))
            throw new McpToolException(new InvalidArgument("id must be a proposal UUID"));

        var state = context.Proposals?.State(id) ?? ProposalState.Unknown;
        return Task.FromResult(new Output(args.Id, state.ToString().ToLowerInvariant()));
    }

    public sealed record Args { public string Id { get; init; } = string.Empty; }
    public sealed record Output(string Id, string State);
}

/// <summary>
/// <c>withdraw_proposal</c> — retract a pending proposal you submitted, before the user applies it
/// (an audit distinction from a user rejection). Returns whether it was withdrawn. Verb class
/// ProposalLifecycle.
/// </summary>
public sealed class WithdrawProposalTool : JsonReadTool<WithdrawProposalTool.Args, WithdrawProposalTool.Output>
{
    public override McpVerbClass VerbClass => McpVerbClass.ProposalLifecycle;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "withdraw_proposal",
        "Withdraw (retract) a pending proposal you submitted, before the user applies it. Returns " +
        "whether it was withdrawn (false if the id is unknown or already resolved).",
        """{"type":"object","properties":{"id":{"type":"string"}},"required":["id"],"additionalProperties":false}""");

    protected override Task<Output> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (!Guid.TryParse(args.Id, out var id))
            throw new McpToolException(new InvalidArgument("id must be a proposal UUID"));

        var withdrew = context.Proposals?.Withdraw(id) ?? false;
        return Task.FromResult(new Output(args.Id, withdrew));
    }

    public sealed record Args { public string Id { get; init; } = string.Empty; }
    public sealed record Output(string Id, bool Withdrew);
}
