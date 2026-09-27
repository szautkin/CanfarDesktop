namespace CanfarDesktop.Mcp.Tools.Builtin;

/// <summary>A snapshot of the app's auth state (supplied by the host via a delegate).</summary>
public sealed record AuthSnapshot(bool IsAuthenticated, string? Username);

/// <summary>
/// <c>describe_app</c> — a static brief of what the app is and what the MCP surface offers, so an
/// agent can orient itself without guessing. No services; the version is injected.
/// </summary>
public sealed class DescribeAppTool : JsonReadTool<EmptyArgs, DescribeAppTool.Output>
{
    private const string Schema = """{"type":"object","properties":{},"additionalProperties":false}""";

    private readonly string _appVersion;

    public DescribeAppTool(string appVersion) => _appVersion = appVersion;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "describe_app",
        "Describe the Verbinal / CanfarDesktop app: what it is and what data it can expose over MCP.",
        Schema);

    protected override Task<Output> HandleAsync(EmptyArgs args, McpToolContext context, CancellationToken cancellationToken)
        => Task.FromResult(new Output(
            "CanfarDesktop (Verbinal)",
            _appVersion,
            "Native Windows client for CADC / CANFAR. Exposes (read-only) observation search " +
            "(ADQL + CAOM2), Skaha sessions, downloaded research observations + notes, VOSpace storage, " +
            "FITS headers/WCS, container image discovery, and a native Jupyter notebook engine. " +
            "If the user is new, or asks what this app does or where anything is, run the built-in " +
            "workflow \"Take the tour of Verbinal\" (use_workflow, builtin:verbinal-onboarding): it " +
            "walks them through all eleven screens with point_at_ui pointing at the controls as you " +
            "describe them, finds and downloads a small image and cube so the viewers are not " +
            "empty, and moves on when each screen's hints have gone (they fade on a timer; pass " +
            "untilClosed to point_at_ui only if the user asks for a slower guide). For one screen in " +
            "depth there is a detailed tour per screen: builtin:tour-search, tour-research, " +
            "tour-fits-viewer, tour-cube-viewer, tour-storage, tour-portal, tour-notebook, " +
            "tour-workflows, tour-ai-guide and tour-remote-compute. Portal, Remote Compute and Storage " +
            "are the user's CADC/CANFAR account and stay locked until they sign in (get_auth_state): " +
            "their tools answer auth-required, and navigate_to asks the user to sign in. Signing in is " +
            "theirs to do; never ask for their password. To guide them through a setting, open_settings " +
            "shows its section and point_at_ui points inside it; settings are theirs to set."));

    public sealed record Output(string Name, string Version, string Summary);
}

/// <summary>
/// <c>get_auth_state</c> — whether the user is signed in to CADC/CANFAR and as whom. Reads a snapshot
/// delegate so it stays pure/testable; the host wires it to the live IAuthService.
/// </summary>
public sealed class GetAuthStateTool : JsonReadTool<EmptyArgs, GetAuthStateTool.Output>
{
    private const string Schema = """{"type":"object","properties":{},"additionalProperties":false}""";

    private readonly Func<AuthSnapshot> _snapshot;

    public GetAuthStateTool(Func<AuthSnapshot> snapshot) => _snapshot = snapshot;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_auth_state",
        "Report whether the user is signed in to CADC/CANFAR, and the signed-in username.",
        Schema);

    protected override Task<Output> HandleAsync(EmptyArgs args, McpToolContext context, CancellationToken cancellationToken)
    {
        var s = _snapshot();
        return Task.FromResult(new Output(s.IsAuthenticated, s.Username));
    }

    public sealed record Output(bool IsAuthenticated, string? Username);
}
