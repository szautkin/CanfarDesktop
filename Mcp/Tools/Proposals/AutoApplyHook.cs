namespace CanfarDesktop.Mcp.Tools.Proposals;

/// <summary>
/// Lets the host opt a just-enqueued write proposal into auto-apply at dispatch time, so the agent's
/// tool call returns an applied RESULT instead of a "queued for review" placeholder. The router calls
/// <see cref="ShouldAutoApply"/> for every write proposal; on true it runs <see cref="Apply"/> and, if
/// that throws, leaves the proposal in the queue for the user to retry/reject. 1-to-1 with the macOS
/// AutoApplyHook.
/// </summary>
public sealed record AutoApplyHook(
    Func<McpVerbClass, PendingProposal, Task<bool>> ShouldAutoApply,
    Func<Guid, Task> Apply);

/// <summary>
/// What the agent gets back after a successful auto-apply: the proposal envelope plus an explicit
/// <see cref="Applied"/> flag so it can branch on "applied" vs "queued for review". 1-to-1 with macOS.
/// </summary>
public sealed record AutoAppliedAck(bool Applied, Guid ProposalId, string Kind, string Summary)
{
    public static AutoAppliedAck From(PendingProposal proposal)
        => new(true, proposal.Id, proposal.Kind, proposal.Summary);
}
