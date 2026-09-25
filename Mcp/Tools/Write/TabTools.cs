namespace CanfarDesktop.Mcp.Tools.Write;

// ─────────────────────────────────────────────────────────────────────────────
// Tab management tools. The open_* tools (open_notebook/open_fits_file/open_cube)
// accumulate viewer tabs with no way to close them from the agent side (QA §6.9).
// These let an agent see what's open, reach a tab that is not the active one, and
// close it. Pure (injected delegates); the host marshals to the UI thread.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Outcome of close_active_tab.</summary>
public sealed record TabCloseOutcome(bool Closed, string Kind, string? Message);

/// <summary>
/// One open viewer tab: the index <c>switch_fits_tab</c> / <c>switch_cube_tab</c> / <c>close_tab</c>
/// take, its display name, and whether it is the one in front.
///
/// <para><see cref="Path"/> is the REAL file path, which is the whole point of listing what is open —
/// a listing that shows only the display name hands back something the caller cannot reopen.</para>
/// </summary>
public sealed record ViewerTabInfo(int Index, string Name, string? Path, bool Active);

/// <summary>Count of open viewer tabs, returned by list_open_tabs (+ per-tab cube/FITS detail).</summary>
public sealed record OpenTabsState(int Notebooks, int FitsViewers, int Cubes,
    IReadOnlyList<ViewerTabInfo>? CubeTabs = null,
    IReadOnlyList<ViewerTabInfo>? FitsTabs = null);

/// <summary>Outcome of closing a tab by index.</summary>
public sealed record TabActionOutcome(bool Ok, string Kind, int? Index, string? Message);

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
        + "WITHOUT a save prompt (autosave keeps a recovery copy). Use close_tab when you mean a tab that "
        + "is not the active one. Live-applied (no proposal).",
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

/// <summary><c>list_open_tabs</c> — what is open, so an agent can reach it and clean up after a run.</summary>
public sealed class ListOpenTabsTool : JsonReadTool<EmptyArgs, OpenTabsState>
{
    private readonly Func<Task<OpenTabsState>> _list;
    public ListOpenTabsTool(Func<Task<OpenTabsState>> list) => _list = list;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "list_open_tabs",
        "List the viewer tabs currently open (notebooks, FITS viewers, cubes). `cubeTabs` and `fitsTabs` "
        + "give each tab's index, name, real file path and whether it is the active one. The index is "
        + "what switch_fits_tab, switch_cube_tab, blink_fits_tabs and close_tab take; the path is what "
        + "open_fits_file / open_cube take if you want it back later.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override Task<OpenTabsState> HandleAsync(EmptyArgs args, McpToolContext context, CancellationToken ct) => _list();
}

/// <summary>
/// <c>close_tab</c> — close a viewer's tab by index, or the active one when no index is given.
///
/// Closing a specific tab used to mean switching to it first and then closing the active one, which
/// is two calls and leaves the viewer somewhere the person did not put it.
/// </summary>
public sealed class CloseTabTool : JsonReadTool<CloseTabTool.Args, TabActionOutcome>
{
    private readonly Func<string, int?, Task<TabActionOutcome>> _close;
    public CloseTabTool(Func<string, int?, Task<TabActionOutcome>> close) => _close = close;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "close_tab",
        "Close a viewer tab. kind: fits | cube. `index` is the 0-based position list_open_tabs reports; "
        + "omit it to close the active tab. Closing shifts the indices of the tabs after it, so close "
        + "from the highest index down when closing several. Live-applied.",
        """{"type":"object","properties":{"kind":{"type":"string","enum":["fits","cube"]},"index":{"type":"integer","minimum":0}},"required":["kind"],"additionalProperties":false}""");

    protected override Task<TabActionOutcome> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var kind = (args.Kind ?? string.Empty).Trim().ToLowerInvariant();
        if (kind is not ("fits" or "cube"))
            throw new McpToolException(new InvalidArgument(
                "kind must be \"fits\" or \"cube\" (notebooks are addressed by name, not by index)"));
        if (args.Index is < 0)
            throw new McpToolException(new InvalidArgument("index is 0-based; it cannot be negative"));

        return _close(kind, args.Index);
    }

    public sealed record Args
    {
        public string? Kind { get; init; }
        public int? Index { get; init; }
    }
}
