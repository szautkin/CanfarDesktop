using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Mcp.Tools;

/// <summary>
/// Exposes an existing tool under a second wire name (the macOS-parity aliases: for example
/// <c>upload_to_vospace</c> → <c>upload_file_to_vospace</c>). The alias carries its own name and
/// description but shares the inner tool's input schema, verb class, agent gating, and invocation —
/// so a write alias produces the same proposal kind and the existing appliers apply it unchanged.
/// </summary>
public sealed class AliasedTool : IMcpTool
{
    private readonly IMcpTool _inner;

    public AliasedTool(string name, string description, IMcpTool inner)
    {
        _inner = inner;
        Descriptor = new ToolDescriptor(name, description, inner.Descriptor.InputSchema);
    }

    public McpVerbClass VerbClass => _inner.VerbClass;
    public bool AgentSafe => _inner.AgentSafe;
    public ToolDescriptor Descriptor { get; }

    public Task<ToolResult> InvokeAsync(JsonValue arguments, McpToolContext context, CancellationToken cancellationToken)
        => _inner.InvokeAsync(arguments, context, cancellationToken);
}
