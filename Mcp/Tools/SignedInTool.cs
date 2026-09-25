using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Mcp.Tools;

/// <summary>
/// A tool that works on the person's CANFAR account, locked the way its screen is until they sign in.
///
/// <para>Signed out, it answers <see cref="AuthRequired"/> without running; signed in, it is the tool
/// it wraps — the same name, schema, verb class and proposal kind, so its applier is unchanged. For
/// the tools whose answers come from this machine rather than the platform, such as the remote compute
/// history and state: nothing on the server would refuse them, so without this an agent could read
/// what the locked screen will not show the person.</para>
/// </summary>
public sealed class SignedInTool : IMcpTool
{
    private readonly IMcpTool _inner;
    private readonly Func<bool> _signedIn;
    private readonly string _detail;

    public SignedInTool(IMcpTool inner, Func<bool> signedIn, string detail)
    {
        _inner = inner;
        _signedIn = signedIn;
        _detail = detail;
    }

    public McpVerbClass VerbClass => _inner.VerbClass;
    public bool AgentSafe => _inner.AgentSafe;
    public ToolDescriptor Descriptor => _inner.Descriptor;

    public Task<ToolResult> InvokeAsync(JsonValue arguments, McpToolContext context, CancellationToken cancellationToken)
        => _signedIn()
            ? _inner.InvokeAsync(arguments, context, cancellationToken)
            : Task.FromResult(ToolResult.Fail(new AuthRequired(_detail)));
}
