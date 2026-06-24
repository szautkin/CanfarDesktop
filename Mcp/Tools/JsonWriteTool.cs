using System.Text.Json;
using CanfarDesktop.Mcp.Tools.Proposals;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Mcp.Tools;

/// <summary>
/// Base class for write tools: deserializes the typed args, applies a timeout, runs <see cref="PlanAsync"/>
/// to produce a <see cref="ProposalPlan"/>, builds a <see cref="PendingProposal"/>, enqueues it to the
/// context's proposal store, and returns <see cref="ProposedResult"/>. The router then decides whether
/// to auto-apply, queue (consuming budget), or refuse. Concrete tools only implement
/// <see cref="VerbClass"/> (SemanticWrite or Destructive), <see cref="Descriptor"/>, and
/// <see cref="PlanAsync"/>. Mirrors the macOS JSONWriteTool. All write tools are agent-safe.
/// </summary>
public abstract class JsonWriteTool<TArgs> : IMcpTool where TArgs : new()
{
    public abstract McpVerbClass VerbClass { get; }
    public bool AgentSafe => true;
    public abstract ToolDescriptor Descriptor { get; }

    protected virtual TimeSpan Timeout => TimeSpan.FromSeconds(60);

    /// <summary>Validate args + build the proposal plan. Throw <see cref="McpToolException"/> for a typed failure.</summary>
    protected abstract Task<ProposalPlan> PlanAsync(TArgs args, McpToolContext context, CancellationToken cancellationToken);

    public async Task<ToolResult> InvokeAsync(JsonValue arguments, McpToolContext context, CancellationToken cancellationToken)
    {
        if (context.Proposals is null)
            return ToolResult.Fail(new BackendError("write tools require a proposal store in the context"));

        TArgs args;
        try
        {
            args = DeserializeArgs(arguments);
        }
        catch (JsonException ex)
        {
            return ToolResult.Fail(new InvalidArgument(ex.Message));
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(Timeout);

            var plan = await PlanAsync(args, context, cts.Token);
            var proposal = PendingProposal.Create(
                Descriptor.Name, plan.Kind, plan.Summary, plan.Payload, context.Origin, context.RequestId);
            context.Proposals.Enqueue(proposal);
            return ToolResult.Proposed(proposal);
        }
        catch (McpToolException ex)
        {
            return ToolResult.Fail(ex.Reason);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ToolResult.Fail(new UpstreamTimeout());
        }
        catch (Exception ex)
        {
            return ToolResult.Fail(new BackendError(ex.Message));
        }
    }

    private static TArgs DeserializeArgs(JsonValue arguments)
    {
        if (arguments is JsonNull) return new TArgs();
        return JsonSerializer.Deserialize<TArgs>(arguments.ToJsonString(), McpJson.Options) ?? new TArgs();
    }
}
