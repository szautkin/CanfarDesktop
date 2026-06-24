using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using CanfarDesktop.Mcp.Audit;
using CanfarDesktop.Mcp.Tools;
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

    /// <summary>The agent-safe tool descriptors exposed to external clients via <c>tools/list</c>.</summary>
    public IReadOnlyList<ToolDescriptor> ExternalManifest { get; }

    public McpToolRouter(IEnumerable<IMcpTool> tools, IAuditSink? audit = null)
    {
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

        var result = await tool.InvokeAsync(arguments, context, cancellationToken);
        Audit(context, name, tool.VerbClass, result is FailedResult ? AuditOutcome.Failed : AuditOutcome.Success, sw, hash);
        return result;
    }

    private void Audit(McpToolContext ctx, string name, McpVerbClass verb, AuditOutcome outcome, Stopwatch sw, string hash)
        => _audit.Record(new AuditEntry(ctx.RequestId, DateTimeOffset.UtcNow, ctx.Origin.Label, name, verb, outcome, sw.ElapsedMilliseconds, hash));

    private static string PayloadHash(JsonValue arguments)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(arguments.ToJsonString()))).ToLowerInvariant();
}
