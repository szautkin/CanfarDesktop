using CanfarDesktop.Services.CubeViewer;

namespace CanfarDesktop.Mcp.Tools.Write;

/// <summary>Result of <c>open_cube</c>: whether the 3D Cube Viewer opened the cube + its dimensions.</summary>
public sealed record CubeOpenOutcome(bool Opened, string? Path, int Nx, int Ny, int Nz, string? Message);

/// <summary>Result of <c>export_cube_figure</c>: whether the figure was written + its path.</summary>
public sealed record CubeExportOutcome(bool Exported, string? Path, string? Message);

/// <summary>The view settings <c>set_cube_view</c> applies (each null is left unchanged).</summary>
public sealed record CubeViewArgs(
    string? Mode, int? Channel, string? Colormap, string? Stretch,
    string? RenderMode, double? WindowLo, double? WindowHi,
    double? Azimuth = null, double? Elevation = null, double? Distance = null,
    double? Density = null, double? SpectralScale = null, int? Steps = null,
    string? Background = null, bool? ShowSlicePlane = null, bool? ShowCaptions = null,
    bool? AutoOrbit = null, bool? Playing = null, bool? ResetCamera = null,
    string? WindowPreset = null,      // "minmax" | "p99" (the two window buttons)
    double? SliceZoom = null,         // slice-view zoom 1–20 (1 = fit)
    int? SliceCenterX = null,         // NATIVE cube pixel to center the slice view on
    int? SliceCenterY = null,
    bool? ResetSliceView = null);     // the double-tap fit reset

/// <summary>Style + destination of an MCP figure export (mirrors the export dialog's controls).</summary>
public sealed record CubeExportRequest(
    string Path, string Format, int Scale, bool Dark,
    string Font = "sans",             // sans | mono | serif
    string TextColor = "auto",        // auto | white | black | cyan | amber
    double TextScale = 1.0,           // 0.75–1.5 (the dialog's text-size slider)
    bool Annotate = true,             // camera/metadata annotations line
    bool Transparent = false);        // transparent background (PNG)

/// <summary>
/// <c>open_cube</c> — open a FITS spectral cube (NAXIS=3) in the 3D Cube Viewer, by local file path or
/// downloaded observation id, and switch to it. Verb class ViewState: live-applied, no proposal.
/// </summary>
public sealed class OpenCubeTool : JsonReadTool<OpenCubeTool.Args, CubeOpenOutcome>
{
    private readonly Func<string, Task<CubeOpenOutcome>> _open;
    public OpenCubeTool(Func<string, Task<CubeOpenOutcome>> open) => _open = open;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "open_cube",
        "Open a FITS spectral cube (NAXIS=3) in the 3D Cube Viewer and switch to it — by local file " +
        "path, or by the id/publisher id of a DOWNLOADED observation (download_observation first). " +
        "Reports the cube dimensions; fails gracefully if the file is not a 3D cube. Live-applied.",
        """{"type":"object","properties":{"path":{"type":"string"},"observationId":{"type":"string"}},"additionalProperties":false}""");

    protected override async Task<CubeOpenOutcome> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var target = (args.Path ?? args.ObservationId ?? string.Empty).Trim();
        if (target.Length == 0)
            throw new McpToolException(new InvalidArgument("path or observationId is required"));
        return await _open(target);
    }

    public sealed record Args { public string? Path { get; init; } public string? ObservationId { get; init; } }
}

/// <summary>
/// <c>set_cube_view</c> — steer the Cube Viewer's display: volume/slice mode, channel, colormap,
/// stretch, render mode (emission/max-intensity), and window levels. Verb class ViewState: live-applied.
/// </summary>
public sealed class SetCubeViewTool : JsonReadTool<SetCubeViewTool.Args, CubeViewState?>
{
    private readonly Func<CubeViewArgs, Task<CubeViewState?>> _apply;
    public SetCubeViewTool(Func<CubeViewArgs, Task<CubeViewState?>> apply) => _apply = apply;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "set_cube_view",
        "Steer the 3D Cube Viewer — every control the UI exposes. Display: view mode, channel, colormap, " +
        "stretch, render mode, window levels (or windowPreset minmax/p99, the two window buttons). Camera: " +
        "azimuth/elevation (radians) and distance (zoom), or resetCamera to recenter. Volume tuning: density " +
        "(opacity 0.1–3), spectralScale (z-stretch 0.5–4), steps (ray-march quality 96–768). Visibility: " +
        "background (dark/black/light), showSlicePlane, showCaptions, autoOrbit. Playback: playing " +
        "(start/stop the channel animation). Slice navigation: sliceZoom (1–20, 1 = fit) with " +
        "sliceCenterX/Y (the NATIVE cube pixel to center on — use to zoom the user into a region), or " +
        "resetSliceView for the fit view. Only the fields you pass change. Returns the resulting cube view " +
        "state. Live-applied.",
        """{"type":"object","properties":{"mode":{"type":"string","enum":["volume","slice"]},"channel":{"type":"integer","minimum":0},"colormap":{"type":"string","enum":["grayscale","inverted","heat","cool","viridis","inferno","magma","plasma"]},"stretch":{"type":"string","enum":["linear","log","sqrt","squared","asinh"]},"renderMode":{"type":"string","enum":["emission","maxIntensity"]},"windowLo":{"type":"number","minimum":0,"maximum":1},"windowHi":{"type":"number","minimum":0,"maximum":1},"windowPreset":{"type":"string","enum":["minmax","p99"]},"azimuth":{"type":"number"},"elevation":{"type":"number"},"distance":{"type":"number","minimum":0.5,"maximum":8},"density":{"type":"number","minimum":0.1,"maximum":3},"spectralScale":{"type":"number","minimum":0.5,"maximum":4},"steps":{"type":"integer","minimum":96,"maximum":768},"background":{"type":"string","enum":["dark","black","light"]},"showSlicePlane":{"type":"boolean"},"showCaptions":{"type":"boolean"},"autoOrbit":{"type":"boolean"},"playing":{"type":"boolean"},"resetCamera":{"type":"boolean"},"sliceZoom":{"type":"number","minimum":1,"maximum":20},"sliceCenterX":{"type":"integer","minimum":0},"sliceCenterY":{"type":"integer","minimum":0},"resetSliceView":{"type":"boolean"}},"additionalProperties":false}""");

    protected override async Task<CubeViewState?> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (args.Channel is < 0)
            throw new McpToolException(new InvalidArgument("channel must be >= 0"));
        if (args.WindowPreset is { } wp && wp.ToLowerInvariant() is not ("minmax" or "p99"))
            throw new McpToolException(new InvalidArgument("windowPreset must be minmax or p99"));
        var state = await _apply(new CubeViewArgs(
            args.Mode, args.Channel, args.Colormap, args.Stretch, args.RenderMode, args.WindowLo, args.WindowHi,
            args.Azimuth, args.Elevation, args.Distance, args.Density, args.SpectralScale, args.Steps,
            args.Background, args.ShowSlicePlane, args.ShowCaptions, args.AutoOrbit, args.Playing, args.ResetCamera,
            args.WindowPreset, args.SliceZoom, args.SliceCenterX, args.SliceCenterY, args.ResetSliceView));
        return state ?? throw new McpToolException(new TargetNotResolved("the cube viewer is not open — open a cube with open_cube first"));
    }

    public sealed record Args
    {
        public string? Mode { get; init; }
        public int? Channel { get; init; }
        public string? Colormap { get; init; }
        public string? Stretch { get; init; }
        public string? RenderMode { get; init; }
        public double? WindowLo { get; init; }
        public double? WindowHi { get; init; }
        public string? WindowPreset { get; init; }
        public double? Azimuth { get; init; }
        public double? Elevation { get; init; }
        public double? Distance { get; init; }
        public double? Density { get; init; }
        public double? SpectralScale { get; init; }
        public int? Steps { get; init; }
        public string? Background { get; init; }
        public bool? ShowSlicePlane { get; init; }
        public bool? ShowCaptions { get; init; }
        public bool? AutoOrbit { get; init; }
        public bool? Playing { get; init; }
        public bool? ResetCamera { get; init; }
        public double? SliceZoom { get; init; }
        public int? SliceCenterX { get; init; }
        public int? SliceCenterY { get; init; }
        public bool? ResetSliceView { get; init; }
    }
}

/// <summary>
/// <c>export_cube_figure</c> — render the current Cube Viewer view to a publication figure (header,
/// framed render with axis captions, legend + colorbar) and write it to a PNG or PDF path.
/// Verb class ViewState: live-applied.
/// </summary>
public sealed class ExportCubeFigureTool : JsonReadTool<ExportCubeFigureTool.Args, CubeExportOutcome>
{
    private readonly Func<CubeExportRequest, Task<CubeExportOutcome>> _export;
    public ExportCubeFigureTool(Func<CubeExportRequest, Task<CubeExportOutcome>> export) => _export = export;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "export_cube_figure",
        "Export the current 3D Cube Viewer view as a publication figure (box + axis captions + legend + " +
        "colorbar) to a local PNG or PDF path, at 2× or 4× resolution. Style options mirror the export " +
        "dialog: theme (dark/light), font (sans/mono/serif), textColor (auto/white/black/cyan/amber), " +
        "textScale (0.75–1.5), annotate (the camera/metadata line), transparent (background, PNG). The " +
        "viewer must be visible. Live-applied.",
        """{"type":"object","properties":{"path":{"type":"string"},"format":{"type":"string","enum":["png","pdf"]},"scale":{"type":"integer","enum":[2,4]},"theme":{"type":"string","enum":["dark","light"]},"font":{"type":"string","enum":["sans","mono","serif"]},"textColor":{"type":"string","enum":["auto","white","black","cyan","amber"]},"textScale":{"type":"number","minimum":0.75,"maximum":1.5},"annotate":{"type":"boolean"},"transparent":{"type":"boolean"}},"required":["path"],"additionalProperties":false}""");

    protected override async Task<CubeExportOutcome> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var path = (args.Path ?? string.Empty).Trim();
        if (path.Length == 0) throw new McpToolException(new InvalidArgument("path is required"));
        var format = string.IsNullOrWhiteSpace(args.Format)
            ? (path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? "pdf" : "png")
            : args.Format!.Trim();
        int scale = args.Scale is 4 ? 4 : 2;
        bool dark = !string.Equals(args.Theme, "light", StringComparison.OrdinalIgnoreCase);
        string font = (args.Font ?? "sans").Trim().ToLowerInvariant();
        if (font is not ("sans" or "mono" or "serif"))
            throw new McpToolException(new InvalidArgument("font must be sans, mono, or serif"));
        string textColor = (args.TextColor ?? "auto").Trim().ToLowerInvariant();
        if (textColor is not ("auto" or "white" or "black" or "cyan" or "amber"))
            throw new McpToolException(new InvalidArgument("textColor must be auto, white, black, cyan, or amber"));
        double textScale = Math.Clamp(args.TextScale ?? 1.0, 0.75, 1.5);
        return await _export(new CubeExportRequest(
            path, format, scale, dark, font, textColor, textScale,
            Annotate: args.Annotate ?? true, Transparent: args.Transparent ?? false));
    }

    public sealed record Args
    {
        public string? Path { get; init; }
        public string? Format { get; init; }
        public int? Scale { get; init; }
        public string? Theme { get; init; }
        public string? Font { get; init; }
        public string? TextColor { get; init; }
        public double? TextScale { get; init; }
        public bool? Annotate { get; init; }
        public bool? Transparent { get; init; }
    }
}

/// <summary>
/// <c>get_cube_view</c> — read the loaded cube + current display state of the 3D Cube Viewer.
/// </summary>
public sealed class GetCubeViewTool : JsonReadTool<GetCubeViewTool.Args, CubeViewState?>
{
    private readonly Func<Task<CubeViewState?>> _get;
    public GetCubeViewTool(Func<Task<CubeViewState?>> get) => _get = get;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_cube_view",
        "Read the full 3D Cube Viewer state — everything the UI shows. File: name/object/instrument + local " +
        "path, NATIVE dimensions (nx/ny; nz is the rendered channel count) plus renderNx/renderNy/nativeNz " +
        "and the downsampled flag. Display: view mode, current channel and its spectral value, colormap, " +
        "stretch, render mode, window levels, unit, data range + display-cut range (cutLo/cutHi), median, " +
        "NaN fraction. Camera/volume: azimuth/elevation/distance, density, spectral scale, ray-march steps, " +
        "background, the slice-plane / captions / auto-orbit toggles, playback state. Slice view: sliceZoom " +
        "+ the native pixel at its center. Spectrum panel: open flag + probed spaxel. Opacity curve: " +
        "transferPoints (set with set_cube_transfer).",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override Task<CubeViewState?> HandleAsync(Args args, McpToolContext context, CancellationToken ct) => _get();

    public sealed record Args { }
}

/// <summary>
/// <c>probe_cube_spectrum</c> — extract the spectrum (flux vs channel) at a spatial pixel of the cube.
/// Coordinates are NATIVE cube pixels (matching get_cube_view's nx/ny); blanked voxels serialize as
/// null flux entries; the failure modes are typed errors instead of an undiagnosable null.
/// </summary>
public sealed class ProbeCubeSpectrumTool : JsonReadTool<ProbeCubeSpectrumTool.Args, CubeSpectrumResult>
{
    private readonly Func<int, int, Task<CubeSpectrumProbe?>> _probe;
    public ProbeCubeSpectrumTool(Func<int, int, Task<CubeSpectrumProbe?>> probe) => _probe = probe;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "probe_cube_spectrum",
        "Extract the spectrum (flux per channel, with the spectral-axis world value per channel) at a " +
        "0-based NATIVE spatial pixel (x, y) of the loaded cube — the pixel grid matching get_cube_view's " +
        "nx/ny; the spectrum has one entry per rendered channel (get_cube_view's nz). Blanked voxels " +
        "(NaN/Inf in the data, e.g. masked cube cells) appear as null flux entries, with the count in " +
        "`blankedChannels` (== the spectrum length for a fully masked spaxel). Also returns the spectral " +
        "conventions when the header has them — `spectralFrame` (SPECSYS, e.g. LSRK; REQUIRED to interpret " +
        "velocities), `restFrequencyGHz` (for frequency↔velocity), and the synthesized beam " +
        "(`beamMajorArcsec`/`beamMinorArcsec`/`beamPaDeg`, for K↔Jy/beam + flux integration). Fails with " +
        "a typed error if no cube is loaded or (x, y) is outside the cube. Returns DATA only — use " +
        "show_cube_spectrum to open the on-screen spectrum panel for the user.",
        """{"type":"object","properties":{"x":{"type":"integer","minimum":0},"y":{"type":"integer","minimum":0}},"required":["x","y"],"additionalProperties":false}""");

    protected override async Task<CubeSpectrumResult> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (args.X is null || args.Y is null)
            throw new McpToolException(new InvalidArgument("x and y are required"));
        if (args.X < 0 || args.Y < 0)
            throw new McpToolException(new InvalidArgument("x and y must be >= 0"));

        var probe = await _probe(args.X.Value, args.Y.Value);
        return probe switch
        {
            null => throw new McpToolException(new BackendError("the cube viewer did not respond (UI dispatch failed)")),
            { Status: CubeProbeStatus.NoCube } => throw new McpToolException(
                new TargetNotResolved("no cube is loaded — open one with open_cube first")),
            { Status: CubeProbeStatus.OutOfRange } p => throw new McpToolException(
                new InvalidArgument($"(x, y) is outside the cube — its spatial grid is {p.Nx} × {p.Ny} pixels (0-based)")),
            _ => probe.Result!,
        };
    }

    public sealed record Args { public int? X { get; init; } public int? Y { get; init; } }
}

/// <summary>Outcome of <c>show_cube_spectrum</c>: whether the panel is now open + the spectrum shown.</summary>
public sealed record ShowCubeSpectrumOutcome(bool PanelOpen, CubeSpectrumResult? Spectrum);

/// <summary>
/// <c>show_cube_spectrum</c> — the agent-side spaxel CLICK: switch to slice mode, probe a native
/// pixel, and open the on-screen spectrum panel so the USER sees it (probe_cube_spectrum only
/// returns data). Verb class ViewState: live-applied.
/// </summary>
public sealed class ShowCubeSpectrumTool : JsonReadTool<ShowCubeSpectrumTool.Args, ShowCubeSpectrumOutcome>
{
    private readonly Func<int, int, Task<CubeSpectrumProbe?>> _show;
    private readonly Func<Task<bool>> _close;
    public ShowCubeSpectrumTool(Func<int, int, Task<CubeSpectrumProbe?>> show, Func<Task<bool>> close)
    { _show = show; _close = close; }

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "show_cube_spectrum",
        "Show the user the spectrum at a spaxel — the agent-side click: switches the Cube Viewer to slice " +
        "mode, probes the 0-based NATIVE pixel (x, y), and opens the on-screen spectrum panel with the " +
        "channel marker. Returns the same spectrum data as probe_cube_spectrum (which shows nothing). " +
        "Pass close:true (no x/y) to dismiss the panel. Live-applied.",
        """{"type":"object","properties":{"x":{"type":"integer","minimum":0},"y":{"type":"integer","minimum":0},"close":{"type":"boolean"}},"additionalProperties":false}""");

    protected override async Task<ShowCubeSpectrumOutcome> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (args.Close == true)
        {
            if (!await _close())
                throw new McpToolException(new TargetNotResolved("the cube viewer is not open"));
            return new ShowCubeSpectrumOutcome(false, null);
        }
        if (args.X is null || args.Y is null)
            throw new McpToolException(new InvalidArgument("x and y are required (or pass close:true)"));
        if (args.X < 0 || args.Y < 0)
            throw new McpToolException(new InvalidArgument("x and y must be >= 0"));

        var probe = await _show(args.X.Value, args.Y.Value);
        return probe switch
        {
            null => throw new McpToolException(new BackendError("the cube viewer did not respond (UI dispatch failed)")),
            { Status: CubeProbeStatus.NoCube } => throw new McpToolException(
                new TargetNotResolved("no cube is loaded — open one with open_cube first")),
            { Status: CubeProbeStatus.OutOfRange } p => throw new McpToolException(
                new InvalidArgument($"(x, y) is outside the cube — its spatial grid is {p.Nx} × {p.Ny} pixels (0-based)")),
            _ => new ShowCubeSpectrumOutcome(true, probe.Result),
        };
    }

    public sealed record Args { public int? X { get; init; } public int? Y { get; init; } public bool? Close { get; init; } }
}

/// <summary>
/// <c>set_cube_transfer</c> — set or reset the volume opacity transfer function (the "Opacity curve"
/// editor: data value → alpha control points). Verb class ViewState: live-applied.
/// </summary>
public sealed class SetCubeTransferTool : JsonReadTool<SetCubeTransferTool.Args, CubeViewState?>
{
    private readonly Func<IReadOnlyList<CubeTransferPoint>?, bool, Task<CubeViewState?>> _apply;
    public SetCubeTransferTool(Func<IReadOnlyList<CubeTransferPoint>?, bool, Task<CubeViewState?>> apply) => _apply = apply;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "set_cube_transfer",
        "Set the Cube Viewer's volume opacity curve (the transfer-function editor): points is the full " +
        "list of control points, each {x, y} in [0,1] — x is the normalized data value, y the opacity. " +
        "At least 2 points; the min/max-x points become the pinned endpoints. Use it to emphasise faint " +
        "structure (raise low-x opacity) or cut noise (zero it). reset:true restores the default curve. " +
        "The current curve is in get_cube_view's transferPoints. Live-applied.",
        """{"type":"object","properties":{"points":{"type":"array","minItems":2,"items":{"type":"object","properties":{"x":{"type":"number","minimum":0,"maximum":1},"y":{"type":"number","minimum":0,"maximum":1}},"required":["x","y"],"additionalProperties":false}},"reset":{"type":"boolean"}},"additionalProperties":false}""");

    protected override async Task<CubeViewState?> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        bool reset = args.Reset == true;
        if (!reset && (args.Points is null || args.Points.Count < 2))
            throw new McpToolException(new InvalidArgument("pass points (>= 2 control points) or reset:true"));
        var points = reset ? null : args.Points!.Select(p => new CubeTransferPoint(p.X, p.Y)).ToList();
        var state = await _apply(points, reset);
        return state ?? throw new McpToolException(new TargetNotResolved("the cube viewer is not open — open a cube with open_cube first"));
    }

    public sealed record Args
    {
        public IReadOnlyList<Point>? Points { get; init; }
        public bool? Reset { get; init; }
        public sealed record Point { public double X { get; init; } public double Y { get; init; } }
    }
}

/// <summary>
/// <c>get_cube_channel_profile</c> — the channel scrubber's waveform data: NaN-aware mean per
/// rendered channel, in physical units, with the spectral axis. Lets an agent find where the
/// signal lives (bright channels) the way the user eyeballs the waveform.
/// </summary>
public sealed class GetCubeChannelProfileTool : JsonReadTool<EmptyArgs, CubeChannelProfileResult>
{
    private readonly Func<Task<CubeChannelProfileResult?>> _get;
    public GetCubeChannelProfileTool(Func<Task<CubeChannelProfileResult?>> get) => _get = get;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_cube_channel_profile",
        "Read the loaded cube's per-channel mean profile (the channel scrubber's waveform): one NaN-aware " +
        "mean per rendered channel (null = fully blanked channel) plus the spectral-axis world value per " +
        "channel. Means are in APPROXIMATE physical units — they average the display-normalized volume, " +
        "whose values are clipped to the display cut — so use them to find WHERE the signal lives (relative " +
        "shape), and probe_cube_spectrum for quantitative values.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override async Task<CubeChannelProfileResult> HandleAsync(EmptyArgs args, McpToolContext context, CancellationToken ct)
        => await _get() ?? throw new McpToolException(
            new TargetNotResolved("no cube is loaded (or it has a single channel) — open one with open_cube first"));
}

/// <summary>Outcome of <c>switch_cube_tab</c>.</summary>
public sealed record CubeTabSwitchOutcome(bool Switched, int Index, int Count, string? ActiveName, string? Message);

/// <summary>
/// <c>switch_cube_tab</c> — make a specific cube tab active (the user clicking a tab header).
/// Verb class ViewState: live-applied.
/// </summary>
public sealed class SwitchCubeTabTool : JsonReadTool<SwitchCubeTabTool.Args, CubeTabSwitchOutcome>
{
    private readonly Func<int, Task<CubeTabSwitchOutcome>> _switch;
    public SwitchCubeTabTool(Func<int, Task<CubeTabSwitchOutcome>> sw) => _switch = sw;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "switch_cube_tab",
        "Switch the Cube Viewer to a specific open tab by 0-based index (list_open_tabs reports the open " +
        "cube tabs with their indices and names). Combine with close_active_tab(kind:cube) to close a " +
        "specific tab: switch to it, then close. Live-applied.",
        """{"type":"object","properties":{"index":{"type":"integer","minimum":0}},"required":["index"],"additionalProperties":false}""");

    protected override Task<CubeTabSwitchOutcome> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (args.Index is null || args.Index < 0)
            throw new McpToolException(new InvalidArgument("index (0-based) is required"));
        return _switch(args.Index.Value);
    }

    public sealed record Args { public int? Index { get; init; } }
}

/// <summary>One recently opened cube, returned by <c>list_recent_cubes</c>.</summary>
public sealed record RecentCubeInfo(string Name, string Path, DateTime OpenedAt);

/// <summary><c>list_recent_cubes</c> — the recently opened cubes the empty-state UI offers.</summary>
public sealed class ListRecentCubesTool : JsonReadTool<EmptyArgs, ListRecentCubesTool.Output>
{
    private readonly Func<Task<IReadOnlyList<RecentCubeInfo>>> _list;
    public ListRecentCubesTool(Func<Task<IReadOnlyList<RecentCubeInfo>>> list) => _list = list;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "list_recent_cubes",
        "List the recently opened FITS cubes (name + local path, newest first) — the same recents the Cube " +
        "Viewer's empty state offers — so you can reopen one with open_cube without asking for a path.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override async Task<Output> HandleAsync(EmptyArgs args, McpToolContext context, CancellationToken ct)
    {
        var entries = await _list();
        return new Output(entries.Count, entries);
    }

    public sealed record Output(int Count, IReadOnlyList<RecentCubeInfo> Recents);
}
