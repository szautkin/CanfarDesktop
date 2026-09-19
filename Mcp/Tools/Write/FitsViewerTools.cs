using CanfarDesktop.Services.Fits;

namespace CanfarDesktop.Mcp.Tools.Write;

/// <summary>The display settings <c>set_fits_view</c> applies to the active FITS tab (each null is unchanged).</summary>
public sealed record FitsViewArgs(
    string? Stretch, string? Colormap, double? MinCut, double? MaxCut,
    double? ZoomPercent, bool? NorthUp, bool? Reset, bool? ClearCrosshair,
    int? Hdu = null,                  // switch the displayed HDU/extension (image HDUs only)
    int? CrosshairX = null,           // place the crosshair at a 0-based display pixel (works without WCS)
    int? CrosshairY = null,
    int? CenterX = null,              // center/pan the viewport on a 0-based display pixel
    int? CenterY = null,
    bool? SyncZoom = null,            // the sync-zoom toolbar toggle
    bool? LinkedCrosshair = null,     // the linked-crosshair toolbar toggle
    bool? ShowHeaderPanel = null,     // show/hide the header + image-info panel
    bool? ShowBookmarksPanel = null); // show/hide the saved-coordinates panel

/// <summary>
/// <c>set_fits_view</c> — steer the 2D FITS viewer's ACTIVE tab: stretch, colormap, black/white cut
/// levels, zoom, North-Up, reset, clear crosshair. Verb class ViewState: live-applied, no proposal.
/// </summary>
public sealed class SetFitsViewTool : JsonReadTool<SetFitsViewTool.Args, FitsViewState?>
{
    private readonly Func<FitsViewArgs, Task<FitsViewState?>> _apply;
    public SetFitsViewTool(Func<FitsViewArgs, Task<FitsViewState?>> apply) => _apply = apply;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "set_fits_view",
        "Steer the 2D FITS viewer's ACTIVE tab — every control the UI exposes. Display: stretch, colormap, " +
        "black/white cut levels (minCut/maxCut, physical pixel units — read current from get_fits_view), " +
        "zoom (percent, 100 = 1:1), North-Up, reset. HDU: hdu switches the displayed extension (image HDUs " +
        "only — get_fits_view lists them). Crosshair: crosshairX/Y places it at a 0-based display pixel " +
        "(works without WCS; fits_goto_coordinate is the RA/Dec route), clearCrosshair removes it. " +
        "Navigation: centerX/Y pans the viewport to a display pixel. Cross-tab: syncZoom and " +
        "linkedCrosshair (the toolbar toggles). Panels: showHeaderPanel (header + image info), " +
        "showBookmarksPanel (saved coordinates). Only the fields you pass change. Returns the resulting " +
        "view state. Live-applied.",
        """{"type":"object","properties":{"stretch":{"type":"string","enum":["linear","log","sqrt","squared","asinh"]},"colormap":{"type":"string","enum":["grayscale","inverted","heat","cool","viridis"]},"minCut":{"type":"number"},"maxCut":{"type":"number"},"zoomPercent":{"type":"number","minimum":5,"maximum":2000},"northUp":{"type":"boolean"},"reset":{"type":"boolean"},"clearCrosshair":{"type":"boolean"},"hdu":{"type":"integer","minimum":0},"crosshairX":{"type":"integer","minimum":0},"crosshairY":{"type":"integer","minimum":0},"centerX":{"type":"integer","minimum":0},"centerY":{"type":"integer","minimum":0},"syncZoom":{"type":"boolean"},"linkedCrosshair":{"type":"boolean"},"showHeaderPanel":{"type":"boolean"},"showBookmarksPanel":{"type":"boolean"}},"additionalProperties":false}""");

    protected override async Task<FitsViewState?> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if ((args.CrosshairX is null) != (args.CrosshairY is null))
            throw new McpToolException(new InvalidArgument("crosshairX and crosshairY must be passed together"));
        if ((args.CenterX is null) != (args.CenterY is null))
            throw new McpToolException(new InvalidArgument("centerX and centerY must be passed together"));
        var state = await _apply(new FitsViewArgs(
            args.Stretch, args.Colormap, args.MinCut, args.MaxCut,
            args.ZoomPercent, args.NorthUp, args.Reset, args.ClearCrosshair,
            args.Hdu, args.CrosshairX, args.CrosshairY, args.CenterX, args.CenterY,
            args.SyncZoom, args.LinkedCrosshair, args.ShowHeaderPanel, args.ShowBookmarksPanel));
        return state ?? throw new McpToolException(new TargetNotResolved("the FITS viewer is not open — open a FITS file first"));
    }

    public sealed record Args
    {
        public string? Stretch { get; init; }
        public string? Colormap { get; init; }
        public double? MinCut { get; init; }
        public double? MaxCut { get; init; }
        public double? ZoomPercent { get; init; }
        public bool? NorthUp { get; init; }
        public bool? Reset { get; init; }
        public bool? ClearCrosshair { get; init; }
        public int? Hdu { get; init; }
        public int? CrosshairX { get; init; }
        public int? CrosshairY { get; init; }
        public int? CenterX { get; init; }
        public int? CenterY { get; init; }
        public bool? SyncZoom { get; init; }
        public bool? LinkedCrosshair { get; init; }
        public bool? ShowHeaderPanel { get; init; }
        public bool? ShowBookmarksPanel { get; init; }
    }
}

/// <summary><c>get_fits_view</c> — read the active FITS tab's file + display state.</summary>
public sealed class GetFitsViewTool : JsonReadTool<GetFitsViewTool.Args, FitsViewState?>
{
    private readonly Func<Task<FitsViewState?>> _get;
    public GetFitsViewTool(Func<Task<FitsViewState?>> get) => _get = get;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_fits_view",
        "Read the 2D FITS viewer's full active-tab state: file name + local path, dimensions, the HDU list " +
        "and displayed extension, stretch/colormap/cut levels, zoom percent, North-Up, WCS presence + pixel " +
        "scale/north angle/parity/approximate flag, pixel unit + data range, the crosshair (sky position AND " +
        "display pixel), the sync-zoom / linked-crosshair toggles, blink status, panel visibility, and the " +
        "status-bar message. Returns null if the FITS viewer is not open.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override Task<FitsViewState?> HandleAsync(Args args, McpToolContext context, CancellationToken ct) => _get();

    public sealed record Args { }
}

/// <summary><c>probe_fits_pixel</c> — read the pixel value + RA/Dec at a display pixel of the active FITS tab.</summary>
public sealed class ProbeFitsPixelTool : JsonReadTool<ProbeFitsPixelTool.Args, FitsPixelResult?>
{
    private readonly Func<int, int, Task<FitsPixelResult?>> _probe;
    public ProbeFitsPixelTool(Func<int, int, Task<FitsPixelResult?>> probe) => _probe = probe;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "probe_fits_pixel",
        "Read the pixel value and sky coordinate (RA/Dec, if the FITS has WCS) at a 0-based display pixel " +
        "(x, y) of the active FITS tab — (0,0) is the top-left. The value carries its physical unit in " +
        "`unit` (the FITS BUNIT) when the header has one; a blanked pixel (NaN/Inf in the data) OMITS " +
        "`value`. Returns null if no FITS is loaded or (x,y) is out of range.",
        """{"type":"object","properties":{"x":{"type":"integer","minimum":0},"y":{"type":"integer","minimum":0}},"required":["x","y"],"additionalProperties":false}""");

    protected override Task<FitsPixelResult?> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (args.X is null || args.Y is null)
            throw new McpToolException(new InvalidArgument("x and y are required"));
        if (args.X < 0 || args.Y < 0)
            throw new McpToolException(new InvalidArgument("x and y must be >= 0"));
        return _probe(args.X.Value, args.Y.Value);
    }

    public sealed record Args { public int? X { get; init; } public int? Y { get; init; } }
}

/// <summary>
/// <c>fits_goto_coordinate</c> — center the active FITS viewport on an RA/Dec and place the crosshair.
/// Verb class ViewState: live-applied.
/// </summary>
public sealed class FitsGotoCoordinateTool : JsonReadTool<FitsGotoCoordinateTool.Args, FitsGotoOutcome>
{
    private readonly Func<double, double, Task<FitsGotoOutcome>> _goto;
    public FitsGotoCoordinateTool(Func<double, double, Task<FitsGotoOutcome>> go) => _goto = go;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "fits_goto_coordinate",
        "Center the active 2D FITS viewport on a sky coordinate (RA/Dec in degrees) and place the crosshair " +
        "there. Requires the loaded FITS to have valid WCS. Distinct from set_search_focus (which targets the " +
        "Search form, not the FITS viewport). Live-applied.",
        """{"type":"object","properties":{"ra":{"type":"number"},"dec":{"type":"number"}},"required":["ra","dec"],"additionalProperties":false}""");

    protected override Task<FitsGotoOutcome> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (args.Ra is null || args.Dec is null)
            throw new McpToolException(new InvalidArgument("ra and dec are required"));
        return _goto(args.Ra.Value, args.Dec.Value);
    }

    public sealed record Args { public double? Ra { get; init; } public double? Dec { get; init; } }
}

/// <summary><c>list_fits_bookmarks</c> — the user's saved FITS sky-coordinate bookmarks.</summary>
public sealed class ListFitsBookmarksTool : JsonReadTool<ListFitsBookmarksTool.Args, ListFitsBookmarksTool.Output>
{
    private readonly Func<Task<IReadOnlyList<FitsBookmark>>> _list;
    public ListFitsBookmarksTool(Func<Task<IReadOnlyList<FitsBookmark>>> list) => _list = list;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "list_fits_bookmarks",
        "List the user's saved FITS sky-coordinate bookmarks (id, label, RA/Dec in degrees, source file). " +
        "Use fits_goto_coordinate with a bookmark's ra/dec to jump the FITS viewport there.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override async Task<Output> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var items = await _list();
        return new Output(items.Count, items);
    }

    public sealed record Args { }
    public sealed record Output(int Count, IReadOnlyList<FitsBookmark> Bookmarks);
}

/// <summary><c>save_fits_bookmark</c> — save a FITS sky-coordinate bookmark. ViewState (live).</summary>
public sealed class SaveFitsBookmarkTool : JsonReadTool<SaveFitsBookmarkTool.Args, FitsBookmark?>
{
    private readonly Func<double, double, string?, string?, Task<FitsBookmark?>> _save;
    public SaveFitsBookmarkTool(Func<double, double, string?, string?, Task<FitsBookmark?>> save) => _save = save;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "save_fits_bookmark",
        "Save a FITS sky-coordinate bookmark (RA/Dec in degrees, optional label + source file). Returns the " +
        "saved bookmark. Live-applied.",
        """{"type":"object","properties":{"ra":{"type":"number"},"dec":{"type":"number"},"label":{"type":"string"},"sourceFile":{"type":"string"}},"required":["ra","dec"],"additionalProperties":false}""");

    protected override Task<FitsBookmark?> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (args.Ra is null || args.Dec is null)
            throw new McpToolException(new InvalidArgument("ra and dec are required"));
        return _save(args.Ra.Value, args.Dec.Value, args.Label, args.SourceFile);
    }

    public sealed record Args
    {
        public double? Ra { get; init; }
        public double? Dec { get; init; }
        public string? Label { get; init; }
        public string? SourceFile { get; init; }
    }
}

/// <summary><c>delete_fits_bookmark</c> — delete a saved FITS bookmark by id. ViewState (live).</summary>
public sealed class DeleteFitsBookmarkTool : JsonReadTool<DeleteFitsBookmarkTool.Args, DeleteFitsBookmarkTool.Output>
{
    private readonly Func<string, Task<bool>> _delete;
    public DeleteFitsBookmarkTool(Func<string, Task<bool>> delete) => _delete = delete;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "delete_fits_bookmark",
        "Delete a saved FITS sky-coordinate bookmark by its id (from list_fits_bookmarks). Returns whether a " +
        "bookmark was found and removed. Live-applied.",
        """{"type":"object","properties":{"id":{"type":"string"}},"required":["id"],"additionalProperties":false}""");

    protected override async Task<Output> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var id = (args.Id ?? string.Empty).Trim();
        if (id.Length == 0) throw new McpToolException(new InvalidArgument("id is required"));
        return new Output(await _delete(id));
    }

    public sealed record Args { public string? Id { get; init; } }
    public sealed record Output(bool Deleted);
}

/// <summary>State of the blink comparison after a <c>blink_fits_tabs</c> call.</summary>
public sealed record FitsBlinkOutcome(bool Active, bool Paused, string? PartnerTab, int IntervalMs, string? Message);

/// <summary>
/// <c>blink_fits_tabs</c> — drive the blink comparison (fade between the active tab and another tab,
/// aligned on the sky): start with a partner tab, adjust the fade interval, pause/resume, stop.
/// Verb class ViewState: live-applied.
/// </summary>
public sealed class BlinkFitsTabsTool : JsonReadTool<BlinkFitsTabsTool.Args, FitsBlinkOutcome>
{
    private readonly Func<string, int?, int?, Task<FitsBlinkOutcome?>> _control;
    public BlinkFitsTabsTool(Func<string, int?, int?, Task<FitsBlinkOutcome?>> control) => _control = control;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "blink_fits_tabs",
        "Blink-compare two open FITS tabs for the user (WCS-aligned fade between the ACTIVE tab and a " +
        "partner): action start (withTabIndex = the partner's 0-based index from list_open_tabs' fitsTabs; " +
        "both images need a valid WCS), pause / resume (freeze the fade), or stop (restore the view). " +
        "intervalMs (500–5000) sets the fade cycle speed with any action. Returns the blink state. " +
        "Live-applied.",
        """{"type":"object","properties":{"action":{"type":"string","enum":["start","stop","pause","resume"]},"withTabIndex":{"type":"integer","minimum":0},"intervalMs":{"type":"integer","minimum":500,"maximum":5000}},"required":["action"],"additionalProperties":false}""");

    protected override async Task<FitsBlinkOutcome> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var action = (args.Action ?? string.Empty).Trim().ToLowerInvariant();
        if (action is not ("start" or "stop" or "pause" or "resume"))
            throw new McpToolException(new InvalidArgument("action must be start, stop, pause, or resume"));
        if (action == "start" && args.WithTabIndex is null)
            throw new McpToolException(new InvalidArgument("start requires withTabIndex (a fitsTabs index from list_open_tabs)"));

        var outcome = await _control(action, args.WithTabIndex, args.IntervalMs);
        return outcome ?? throw new McpToolException(new TargetNotResolved("the FITS viewer is not open — open FITS files first"));
    }

    public sealed record Args
    {
        public string? Action { get; init; }
        public int? WithTabIndex { get; init; }
        public int? IntervalMs { get; init; }
    }
}

/// <summary>Outcome of <c>switch_fits_tab</c>.</summary>
public sealed record FitsTabSwitchOutcome(bool Switched, int Index, int Count, string? ActiveName, string? Message);

/// <summary>
/// <c>switch_fits_tab</c> — make a specific FITS tab active (the user clicking a tab header).
/// Verb class ViewState: live-applied.
/// </summary>
public sealed class SwitchFitsTabTool : JsonReadTool<SwitchFitsTabTool.Args, FitsTabSwitchOutcome>
{
    private readonly Func<int, Task<FitsTabSwitchOutcome>> _switch;
    public SwitchFitsTabTool(Func<int, Task<FitsTabSwitchOutcome>> sw) => _switch = sw;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "switch_fits_tab",
        "Switch the 2D FITS viewer to a specific open tab by 0-based index (list_open_tabs reports the open " +
        "FITS tabs with their indices and names). Combine with close_active_tab(kind:fits) to close a " +
        "specific tab: switch to it, then close. Live-applied.",
        """{"type":"object","properties":{"index":{"type":"integer","minimum":0}},"required":["index"],"additionalProperties":false}""");

    protected override Task<FitsTabSwitchOutcome> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (args.Index is null || args.Index < 0)
            throw new McpToolException(new InvalidArgument("index (0-based) is required"));
        return _switch(args.Index.Value);
    }

    public sealed record Args { public int? Index { get; init; } }
}
