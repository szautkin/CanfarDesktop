namespace CanfarDesktop.Mcp.Tools.Write;

// ─────────────────────────────────────────────────────────────────────────────
// Tab management tools. The open_* tools (open_notebook/open_fits_file/open_cube)
// accumulate viewer tabs with no way to close them from the agent side (QA §6.9).
// These let an agent see what's open and close the active tab of a viewer. Pure
// (injected delegates); the host marshals to the UI thread + calls the tab host.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Outcome of close_active_tab.</summary>
public sealed record TabCloseOutcome(bool Closed, string Kind, string? Message);

/// <summary>One open viewer tab (index for switch_cube_tab/switch_fits_tab, name, active flag).</summary>
public sealed record ViewerTabInfo(int Index, string Name, bool Active);

/// <summary>Count of open viewer tabs, returned by list_open_tabs (+ per-tab cube/FITS detail).</summary>
public sealed record OpenTabsState(int Notebooks, int FitsViewers, int Cubes,
    IReadOnlyList<ViewerTabInfo>? CubeTabs = null,
    IReadOnlyList<ViewerTabInfo>? FitsTabs = null);

/// <summary><c>close_active_tab</c> — close the active tab of a viewer. Verb class ViewState (live).</summary>
public sealed class CloseActiveTabTool : JsonReadTool<CloseActiveTabTool.Args, TabCloseOutcome>
{
    private readonly Func<string, Task<TabCloseOutcome>> _close;
    public CloseActiveTabTool(Func<string, Task<TabCloseOutcome>> close) => _close = close;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "close_active_tab",
        "Close the active tab of a viewer to free it — the open_notebook/open_fits_file/open_cube tools "
        + "accumulate tabs with no other way to close them. kind: notebook | fits | cube. A notebook closes "
        + "WITHOUT a save prompt (autosave keeps a recovery copy). Live-applied (no proposal).",
        """{"type":"object","properties":{"kind":{"type":"string","enum":["notebook","fits","cube"]}},"required":["kind"],"additionalProperties":false}""");

    protected override Task<TabCloseOutcome> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var kind = (args.Kind ?? string.Empty).Trim().ToLowerInvariant();
        if (kind is not ("notebook" or "fits" or "cube"))
            throw new McpToolException(new InvalidArgument("kind must be notebook, fits, or cube"));
        return _close(kind);
    }

    public sealed record Args { public string? Kind { get; init; } }
}

/// <summary><c>list_open_tabs</c> — count the open viewer tabs so the agent can clean up after a run.</summary>
public sealed class ListOpenTabsTool : JsonReadTool<EmptyArgs, OpenTabsState>
{
    private readonly Func<Task<OpenTabsState>> _list;
    public ListOpenTabsTool(Func<Task<OpenTabsState>> list) => _list = list;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "list_open_tabs",
        "Count the viewer tabs currently open (notebooks, FITS viewers, cubes). cubeTabs / fitsTabs list "
        + "each open cube/FITS tab's index/name/active flag for switch_cube_tab / switch_fits_tab. Use "
        + "with close_active_tab to clean up tabs an automated run accumulated.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override Task<OpenTabsState> HandleAsync(EmptyArgs args, McpToolContext context, CancellationToken ct) => _list();
}
