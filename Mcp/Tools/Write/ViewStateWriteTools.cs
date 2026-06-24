namespace CanfarDesktop.Mcp.Tools.Write;

/// <summary>Result of a <c>navigate_to</c>: whether the app switched, and the resolved mode + title.</summary>
public sealed record NavigationOutcome(bool Navigated, string Mode, string ModeTitle);

/// <summary>
/// <c>navigate_to</c> — switch the app to a top-level mode so the user is looking at the relevant view
/// (e.g. after an agent saves a query, send them to Search). Verb class ViewState: live-applied, no
/// proposal, no budget. The host supplies the navigation via the injected closure. 1-to-1 with macOS.
/// </summary>
public sealed class NavigateToTool : JsonReadTool<NavigateToTool.Args, NavigationOutcome>
{
    private readonly Func<string, Task<NavigationOutcome>> _navigate;

    public NavigateToTool(Func<string, Task<NavigationOutcome>> navigate) => _navigate = navigate;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "navigate_to",
        "Switch the app to a top-level mode so the user sees the relevant view. Live-applied (no proposal).",
        """{"type":"object","properties":{"mode":{"type":"string","enum":["landing","portal","search","research","storage","notebook","fitsViewer"]}},"required":["mode"],"additionalProperties":false}""");

    protected override async Task<NavigationOutcome> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args.Mode))
            throw new McpToolException(new InvalidArgument("mode is required"));
        return await _navigate(args.Mode.Trim());
    }

    public sealed record Args { public string Mode { get; init; } = string.Empty; }
}

/// <summary>
/// <c>set_search_focus</c> — point the Search form at a sky position (RA/Dec in degrees) and show it, so
/// the user can refine an agent-suggested cone. Verb class ViewState: live-applied. 1-to-1 with macOS.
/// </summary>
public sealed class SetSearchFocusTool : JsonReadTool<SetSearchFocusTool.Args, SetSearchFocusTool.Output>
{
    private readonly Func<double, double, Task> _apply;

    public SetSearchFocusTool(Func<double, double, Task> apply) => _apply = apply;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "set_search_focus",
        "Point the Search form at a sky position (ICRS RA/Dec in degrees) and bring it into view. " +
        "Live-applied (no proposal).",
        """{"type":"object","properties":{"raDeg":{"type":"number"},"decDeg":{"type":"number"}},"required":["raDeg","decDeg"],"additionalProperties":false}""");

    protected override async Task<Output> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (args.RaDeg is null || args.DecDeg is null)
            throw new McpToolException(new InvalidArgument("raDeg and decDeg are required"));
        if (args.RaDeg is < 0 or > 360)
            throw new McpToolException(new InvalidArgument("raDeg must be in [0, 360]"));
        if (args.DecDeg is < -90 or > 90)
            throw new McpToolException(new InvalidArgument("decDeg must be in [-90, 90]"));

        await _apply(args.RaDeg.Value, args.DecDeg.Value);
        return new Output(true, args.RaDeg.Value, args.DecDeg.Value);
    }

    public sealed record Args
    {
        public double? RaDeg { get; init; }
        public double? DecDeg { get; init; }
    }

    public sealed record Output(bool Applied, double RaDeg, double DecDeg);
}
