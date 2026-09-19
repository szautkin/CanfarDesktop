namespace CanfarDesktop.Mcp.Tools.Write;

// ─────────────────────────────────────────────────────────────────────────────
// Tab management tools. The open_* tools (open_notebook/open_fits_file/open_cube)
// accumulate viewer tabs with no way to close them from the agent side (QA §6.9).
// These let an agent see what's open and close the active tab of a viewer. Pure
// (injected delegates); the host marshals to the UI thread + calls the tab host.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Outcome of close_active_tab.</summary>
public sealed record TabCloseOutcome(bool Closed, string Kind, string? Message);

/// <summary>One open viewer tab, as an agent addresses it.</summary>
public sealed record ViewerTabInfo(int Index, string Name, string? Path, bool Active);

/// <summary>
/// The open viewer tabs, returned by list_open_tabs.
///
/// The counts came first and are kept, but a count is enough to know there is more than one tab and
/// not enough to reach it — no index, no name, no path, and so no way to switch to one or to blink
/// two against each other. The per-viewer lists are what made those possible.
/// </summary>
public sealed record OpenTabsState(
    int Notebooks, int FitsViewers, int Cubes,
    IReadOnlyList<ViewerTabInfo>? FitsTabs = null,
    IReadOnlyList<ViewerTabInfo>? CubeTabs = null);

/// <summary>Outcome of switching or closing a tab by index.</summary>
public sealed record TabActionOutcome(bool Ok, string Kind, int? Index, string? Message);

/// <summary>Outcome of starting or stopping a blink.</summary>
public sealed record BlinkOutcome(bool Blinking, int? IndexA, int? IndexB, string? Message);

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
        "List the viewer tabs currently open. Returns counts for each kind, plus `fitsTabs` and "
        + "`cubeTabs` giving each tab's index, name, real file path and whether it is the active one. "
        + "The index is what switch_tab, close_tab and blink_fits_tabs take.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override Task<OpenTabsState> HandleAsync(EmptyArgs args, McpToolContext context, CancellationToken ct) => _list();
}

/// <summary>
/// <c>switch_tab</c> — make a viewer's tab at a given index the active one.
///
/// Every other viewer tool acts on the ACTIVE tab, which meant that with three FITS files open an
/// agent could read and steer exactly one of them and had no way to reach the others.
/// </summary>
public sealed class SwitchTabTool : JsonReadTool<SwitchTabTool.Args, TabActionOutcome>
{
    private readonly Func<string, int, Task<TabActionOutcome>> _switch;
    public SwitchTabTool(Func<string, int, Task<TabActionOutcome>> to) => _switch = to;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "switch_tab",
        "Make a viewer's tab the active one, so the other tools act on it. kind: fits | cube. `index` "
        + "is the 0-based position list_open_tabs reports. Every other viewer tool reads and steers the "
        + "ACTIVE tab, so this is how you reach a file that is open but not in front. Live-applied.",
        """{"type":"object","properties":{"kind":{"type":"string","enum":["fits","cube"]},"index":{"type":"integer","minimum":0}},"required":["kind","index"],"additionalProperties":false}""");

    protected override Task<TabActionOutcome> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var kind = Kinds.Require(args.Kind);
        if (args.Index is not int index)
            throw new McpToolException(new InvalidArgument("index is required"));
        if (index < 0)
            throw new McpToolException(new InvalidArgument("index is 0-based; it cannot be negative"));
        return _switch(kind, index);
    }

    public sealed record Args
    {
        public string? Kind { get; init; }
        public int? Index { get; init; }
    }
}

/// <summary>
/// <c>close_tab</c> — close a viewer's tab by index, or the active one when no index is given.
///
/// <c>close_active_tab</c> could only ever close what was in front, so cleaning up after a run that
/// opened five files meant switching to each one first.
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
        var kind = Kinds.Require(args.Kind);
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

/// <summary>
/// <c>blink_fits_tabs</c> — the WCS-aligned blink comparison, which is how a transient is found and
/// had no tool at all.
///
/// Alignment is on the SKY, not on the pixels: the two frames can have different scales, rotations and
/// reference pixels and still land on top of each other, which is the whole reason the feature exists
/// and the reason both images need a valid WCS.
/// </summary>
public sealed class BlinkFitsTabsTool : JsonReadTool<BlinkFitsTabsTool.Args, BlinkOutcome>
{
    private readonly Func<int?, int?, bool, Task<BlinkOutcome>> _blink;
    public BlinkFitsTabsTool(Func<int?, int?, bool, Task<BlinkOutcome>> blink) => _blink = blink;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "blink_fits_tabs",
        "Blink two open FITS tabs against each other, aligned on the SKY rather than on their pixels — "
        + "the comparison a transient is found with. `indexA` and `indexB` are 0-based positions from "
        + "list_open_tabs; A becomes the active tab and B is faded over it. Both images need a valid "
        + "WCS. Pass `stop: true` to end a running blink. Live-applied.",
        """{"type":"object","properties":{"indexA":{"type":"integer","minimum":0},"indexB":{"type":"integer","minimum":0},"stop":{"type":"boolean"}},"additionalProperties":false}""");

    protected override Task<BlinkOutcome> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var stop = args.Stop ?? false;
        if (!stop && (args.IndexA is null || args.IndexB is null))
            throw new McpToolException(new InvalidArgument(
                "indexA and indexB are both required to start a blink (or pass stop: true to end one)"));
        if (args.IndexA is < 0 || args.IndexB is < 0)
            throw new McpToolException(new InvalidArgument("indices are 0-based; they cannot be negative"));

        return _blink(args.IndexA, args.IndexB, stop);
    }

    public sealed record Args
    {
        public int? IndexA { get; init; }
        public int? IndexB { get; init; }
        public bool? Stop { get; init; }
    }
}

/// <summary>
/// The viewer kinds the index-addressed tab tools accept, in one place.
///
/// Notebooks are absent deliberately: they have their own tool family that already addresses a
/// notebook by name, so a second way to reach them by index would be two answers to one question.
/// </summary>
internal static class Kinds
{
    public static string Require(string? kind)
    {
        var k = (kind ?? string.Empty).Trim().ToLowerInvariant();
        if (k is not ("fits" or "cube"))
            throw new McpToolException(new InvalidArgument(
                "kind must be \"fits\" or \"cube\" (notebooks are addressed by name, not by index)"));
        return k;
    }
}
