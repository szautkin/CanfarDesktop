using CanfarDesktop.Mcp.Tools.Proposals;

namespace CanfarDesktop.Mcp.Tools.Write;

/// <summary>What a background apply answered with: the id to ask about, and where it had got to.</summary>
public sealed record BackgroundApplyOutcome(
    bool Started, string? JobId, string? Kind, string? Summary, string Status, string? Message = null)
{
    public static BackgroundApplyOutcome Refused(string message)
        => new(false, null, null, null, "notStarted", message);
}

/// <summary>
/// <c>start_background_apply</c> — apply a pending proposal WITHOUT holding the call open.
///
/// Some applies take longer than a JSON-RPC request should wait: a large VOSpace transfer, a whole
/// observation download. Applied inline, the client gives up at its own timeout and the work vanishes —
/// no id, no progress, no error, and no way for the caller to tell whether it is still running. The
/// VOSpace tool descriptions say as much today, and advise re-polling a listing and hoping the bytes
/// turn up.
///
/// This answers immediately with the job id, which is the PROPOSAL id — the work already has an
/// identifier the agent is holding, and one identifier is easier to follow than two. Then
/// <c>get_job_status</c> reports how it is getting on.
/// </summary>
public sealed class StartBackgroundApplyTool : JsonReadTool<StartBackgroundApplyTool.Args, BackgroundApplyOutcome>
{
    private readonly Func<string, Task<BackgroundApplyOutcome>> _start;

    public StartBackgroundApplyTool(Func<string, Task<BackgroundApplyOutcome>> start) => _start = start;

    /// <summary>
    /// ViewState rather than a write of its own: the write was the proposal. But a proposal waiting in
    /// the queue is waiting for the PERSON — this may start only what auto-apply would have applied
    /// without them (<see cref="BackgroundApplyRunner"/>), never a destructive change, and nothing at all
    /// while auto-apply is off. It used to start anything pending: a file removal waiting for approval
    /// went ahead the moment an agent asked.
    /// </summary>
    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "start_background_apply",
        "Apply a pending proposal in the background instead of waiting for it. Use it for work that " +
        "takes longer than a tool call should be held open — a large VOSpace upload or download, or a " +
        "whole-observation download — where waiting inline risks the client timing out on work that is " +
        "actually still running. Answers at once with a job id (the proposal's own id); follow it with " +
        "get_job_status. For quick applies, apply normally: a job is more to keep track of. Only for what " +
        "auto-apply would apply without the person: never a destructive change (a delete, a stop), and " +
        "nothing while auto-apply is off — those wait for the person to approve them in Verbinal, and " +
        "this says so.",
        """
        {"type":"object","properties":{
          "proposalId":{"type":"string","description":"A pending proposal's id (from list_pending_proposals)."}
        },"required":["proposalId"],"additionalProperties":false}
        """);

    protected override async Task<BackgroundApplyOutcome> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var id = (args.ProposalId ?? string.Empty).Trim();
        if (id.Length == 0) throw new McpToolException(new InvalidArgument("proposalId is required"));

        return await _start(id);
    }

    public sealed record Args { public string? ProposalId { get; init; } }
}

/// <summary>
/// Starting a proposal's apply on a background task, and recording where it got to.
///
/// Separate from the tool so the tool stays a lambda away from the host, and so this can be tested
/// against a fake applier without a server.
/// </summary>
public sealed class BackgroundApplyRunner
{
    private readonly IProposalStore _proposals;
    private readonly ProposalApplierRegistry _appliers;
    private readonly JobRegistry _jobs;
    private readonly Func<string, McpVerbClass> _verbOf;
    private readonly Func<bool> _autoApplyEnabled;

    /// <param name="verbOf">What kind of write a proposal kind is — its tool's verb class; Destructive for a kind no tool claims.</param>
    /// <param name="autoApplyEnabled">The person's auto-apply setting, as it is now.</param>
    public BackgroundApplyRunner(IProposalStore proposals, ProposalApplierRegistry appliers, JobRegistry jobs,
                                 Func<string, McpVerbClass> verbOf, Func<bool> autoApplyEnabled)
    {
        _proposals = proposals;
        _appliers = appliers;
        _jobs = jobs;
        _verbOf = verbOf;
        _autoApplyEnabled = autoApplyEnabled;
    }

    public Task<BackgroundApplyOutcome> StartAsync(string proposalId)
    {
        if (!Guid.TryParse(proposalId, out var id))
            return Task.FromResult(BackgroundApplyOutcome.Refused(
                $"'{proposalId}' is not a proposal id — list_pending_proposals reports them"));

        var proposal = _proposals.Get(id);
        if (proposal is null)
            return Task.FromResult(BackgroundApplyOutcome.Refused(
                $"no pending proposal '{proposalId}' — list_pending_proposals shows the ones there are"));

        var applier = _appliers.ApplierFor(proposal.Kind);
        if (applier is null)
            return Task.FromResult(BackgroundApplyOutcome.Refused(
                $"'{proposal.Kind}' has no applier, so it cannot be applied at all — in the background or otherwise"));

        // The person's approval is not the agent's to give: only what auto-apply would have applied without
        // them — the same rule, AutoApplyPolicy — may be started here.
        var verb = _verbOf(proposal.Kind);
        if (!AutoApplyPolicy.ShouldAutoApply(_autoApplyEnabled(), verb))
            return Task.FromResult(BackgroundApplyOutcome.Refused(verb == McpVerbClass.Destructive
                ? $"'{proposal.Kind}' is a destructive change, so only the person can apply it: it waits for their approval in Verbinal's pending changes"
                : $"auto-apply is off, so the person applies each change: '{proposal.Kind}' waits for their approval in Verbinal's pending changes"));

        var job = _jobs.Start(proposal.Id.ToString(), proposal.Kind, proposal.Summary);

        // Deliberately not awaited: answering at once is the entire point. The continuation records the
        // outcome, so a failure is reported by get_job_status rather than disappearing into a dropped
        // task — which is the failure mode this whole mechanism exists to remove.
        _ = Task.Run(async () =>
        {
            try
            {
                await applier.ApplyAsync(proposal);
                _proposals.MarkApplied(proposal.Id);
                _jobs.Finish(proposal.Id.ToString(), succeeded: true, message: null);
            }
            catch (Exception ex)
            {
                _proposals.MarkRejected(proposal.Id);
                _jobs.Finish(proposal.Id.ToString(), succeeded: false, message: ex.Message);
            }
        });

        return Task.FromResult(new BackgroundApplyOutcome(
            true, job.Id, job.Kind, job.Summary, "running",
            "started; ask get_job_status with this id"));
    }
}
