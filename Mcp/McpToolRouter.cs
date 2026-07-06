using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CanfarDesktop.Mcp.Audit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Proposals;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Mcp;

/// <summary>
/// Immutable tool table + dispatcher. Builds the name→tool map (throws on duplicates) and the
/// external manifest (agent-safe tools only), enforces the external-caller gate as defence-in-depth,
/// and emits a PII-safe audit entry per dispatch. 1-to-1 with the macOS AIToolRouter.
/// </summary>
public sealed class McpToolRouter
{
    private readonly IReadOnlyDictionary<string, IMcpTool> _tools;
    private readonly IAuditSink _audit;
    private readonly AutoApplyHook? _autoApplyHook;
    private readonly Func<string, Task>? _onAgentActivity;
    private readonly Action<string>? _onAgentDispatchStart;

    /// <summary>The agent-safe tool descriptors exposed to external clients via <c>tools/list</c>.</summary>
    public IReadOnlyList<ToolDescriptor> ExternalManifest { get; }

    public McpToolRouter(
        IEnumerable<IMcpTool> tools,
        IAuditSink? audit = null,
        AutoApplyHook? autoApplyHook = null,
        Func<string, Task>? onAgentActivity = null,
        Action<string>? onAgentDispatchStart = null)
    {
        _autoApplyHook = autoApplyHook;
        _onAgentActivity = onAgentActivity;
        _onAgentDispatchStart = onAgentDispatchStart;
        var dict = new Dictionary<string, IMcpTool>(StringComparer.Ordinal);
        foreach (var tool in tools)
        {
            if (!dict.TryAdd(tool.Descriptor.Name, tool))
                throw new InvalidOperationException($"Duplicate MCP tool name '{tool.Descriptor.Name}'");
        }

        _tools = dict;
        _audit = audit ?? new LoggingAuditSink();
        ExternalManifest = dict.Values
            .Where(t => t.AgentSafe)
            .Select(t => t.Descriptor)
            .OrderBy(d => d.Name, StringComparer.Ordinal)
            .ToList();
    }

    public IReadOnlyCollection<string> ToolNames => _tools.Keys.ToList();

    public async Task<ToolResult> DispatchAsync(string name, JsonValue arguments, McpToolContext context, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var hash = PayloadHash(arguments);

        if (!_tools.TryGetValue(name, out var tool))
        {
            Audit(context, name, McpVerbClass.Read, AuditOutcome.Unknown, sw, hash);
            return ToolResult.Fail(new UnknownTarget(name));
        }

        // External callers can't reach user-only tools — and shouldn't learn they exist.
        if (context.Origin.IsExternal && !tool.AgentSafe)
        {
            Audit(context, name, tool.VerbClass, AuditOutcome.Rejected, sw, hash);
            return ToolResult.Fail(new UnknownTarget(name));
        }

        // An agent call is starting: pulse the "agent is working" indicator now (before the call runs,
        // so it shows during slow/failing calls too). Best-effort, synchronous, external callers only.
        if (context.Origin.IsExternal)
        {
            try { _onAgentDispatchStart?.Invoke(name); }
            catch { /* indicator is best-effort */ }
        }

        var result = await tool.InvokeAsync(arguments, context, cancellationToken);

        if (result is ProposedResult proposed)
            return await ResolveProposalAsync(tool, proposed.Proposal, context, sw, hash);

        // Successful agent read/non-write call: let the host follow the activity (navigate to the
        // relevant module) so the user can see what the agent is doing. Fire-and-forget — the callback
        // swallows its own errors and never blocks or fails the tool response. Writes navigate on the
        // apply path instead (see McpHost.FollowActivity).
        if (context.Origin.IsExternal && result is not FailedResult && _onAgentActivity is { } activity)
            _ = activity(name);

        Audit(context, name, tool.VerbClass, result is FailedResult ? AuditOutcome.Failed : AuditOutcome.Success, sw, hash);
        return result;
    }

    /// <summary>
    /// The write-path decision after a tool enqueues a proposal: non-write verb classes pass through;
    /// SemanticWrite/Destructive either auto-apply (host hook opts in) or queue, consuming per-turn
    /// budget and withdrawing the proposal when the cap is hit. 1-to-1 with the macOS router.
    /// </summary>
    private async Task<ToolResult> ResolveProposalAsync(IMcpTool tool, PendingProposal proposal, McpToolContext context, Stopwatch sw, string hash)
    {
        var verb = tool.VerbClass;
        var name = tool.Descriptor.Name;

        // Non-write proposals (rare) bypass the budget/auto-apply machinery.
        if (verb is not (McpVerbClass.SemanticWrite or McpVerbClass.Destructive))
        {
            Audit(context, name, verb, AuditOutcome.Proposed, sw, hash);
            return ToolResult.Proposed(proposal);
        }

        // Auto-apply path: the host opts this proposal into immediate apply.
        if (_autoApplyHook is { } hook && await hook.ShouldAutoApply(verb, proposal))
        {
            try
            {
                await hook.Apply(proposal.Id);
                Audit(context, name, verb, AuditOutcome.Applied, sw, hash);
                return ToolResult.Ok(JsonSerializer.SerializeToUtf8Bytes(AutoAppliedAck.From(proposal), McpJson.Options));
            }
            catch (Exception ex)
            {
                // Withdraw the proposal: an auto-apply that already failed at the backend can only
                // fail identically when applied from the strip — leaving it queued just hands the
                // user a doomed item to clean up. The error goes back to the agent, which can fix
                // the arguments and re-invoke the tool.
                context.Proposals?.Withdraw(proposal.Id);
                Audit(context, name, verb, AuditOutcome.Failed, sw, hash);
                return ToolResult.Fail(new BackendError($"auto-apply failed: {ex.Message}"));
            }
        }

        // Queue path: consume per-turn budget; on refusal, withdraw so no partial batch lands.
        if (context.Budget is { } budget && !budget.TryAccept(context.Origin))
        {
            context.Proposals?.Withdraw(proposal.Id);
            Audit(context, name, verb, AuditOutcome.Failed, sw, hash);
            return ToolResult.Fail(new PerTurnProposalCapExceeded(budget.Limit));
        }

        Audit(context, name, verb, AuditOutcome.Proposed, sw, hash);
        return ToolResult.Proposed(proposal);
    }

    private void Audit(McpToolContext ctx, string name, McpVerbClass verb, AuditOutcome outcome, Stopwatch sw, string hash)
        => _audit.Record(new AuditEntry(ctx.RequestId, DateTimeOffset.UtcNow, ctx.Origin.Label, name, verb, outcome, sw.ElapsedMilliseconds, hash));

    private static string PayloadHash(JsonValue arguments)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(arguments.ToJsonString()))).ToLowerInvariant();
}
