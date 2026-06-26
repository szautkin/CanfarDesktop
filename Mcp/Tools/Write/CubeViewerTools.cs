using CanfarDesktop.Services.CubeViewer;

namespace CanfarDesktop.Mcp.Tools.Write;

/// <summary>Result of <c>open_cube</c>: whether the 3D Cube Viewer opened the cube + its dimensions.</summary>
public sealed record CubeOpenOutcome(bool Opened, string? Path, int Nx, int Ny, int Nz, string? Message);

/// <summary>Result of <c>export_cube_figure</c>: whether the figure was written + its path.</summary>
public sealed record CubeExportOutcome(bool Exported, string? Path, string? Message);

/// <summary>The view settings <c>set_cube_view</c> applies (each null is left unchanged).</summary>
public sealed record CubeViewArgs(
    string? Mode, int? Channel, string? Colormap, string? Stretch,
    string? RenderMode, double? WindowLo, double? WindowHi);

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
        "Steer the 3D Cube Viewer: set view mode, channel, colormap, stretch, render mode, and window " +
        "levels. Only the fields you pass change. Returns the resulting cube view state. Live-applied.",
        """{"type":"object","properties":{"mode":{"type":"string","enum":["volume","slice"]},"channel":{"type":"integer","minimum":0},"colormap":{"type":"string","enum":["grayscale","inverted","heat","cool","viridis","inferno","magma","plasma"]},"stretch":{"type":"string","enum":["linear","log","sqrt","squared","asinh"]},"renderMode":{"type":"string","enum":["emission","maxIntensity"]},"windowLo":{"type":"number","minimum":0,"maximum":1},"windowHi":{"type":"number","minimum":0,"maximum":1}},"additionalProperties":false}""");

    protected override async Task<CubeViewState?> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (args.Channel is < 0)
            throw new McpToolException(new InvalidArgument("channel must be >= 0"));
        return await _apply(new CubeViewArgs(
            args.Mode, args.Channel, args.Colormap, args.Stretch, args.RenderMode, args.WindowLo, args.WindowHi));
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
    }
}

/// <summary>
/// <c>export_cube_figure</c> — render the current Cube Viewer view to a publication figure (header,
/// framed render with axis captions, legend + colorbar) and write it to a PNG or PDF path.
/// Verb class ViewState: live-applied.
/// </summary>
public sealed class ExportCubeFigureTool : JsonReadTool<ExportCubeFigureTool.Args, CubeExportOutcome>
{
    private readonly Func<string, string, int, bool, Task<CubeExportOutcome>> _export;
    public ExportCubeFigureTool(Func<string, string, int, bool, Task<CubeExportOutcome>> export) => _export = export;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "export_cube_figure",
        "Export the current 3D Cube Viewer view as a publication figure (box + axis captions + legend + " +
        "colorbar) to a local PNG or PDF path, at 2× or 4× resolution, dark or light theme. The viewer " +
        "must be visible. Live-applied.",
        """{"type":"object","properties":{"path":{"type":"string"},"format":{"type":"string","enum":["png","pdf"]},"scale":{"type":"integer","enum":[2,4]},"theme":{"type":"string","enum":["dark","light"]}},"required":["path"],"additionalProperties":false}""");

    protected override async Task<CubeExportOutcome> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var path = (args.Path ?? string.Empty).Trim();
        if (path.Length == 0) throw new McpToolException(new InvalidArgument("path is required"));
        var format = string.IsNullOrWhiteSpace(args.Format)
            ? (path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? "pdf" : "png")
            : args.Format!.Trim();
        int scale = args.Scale is 4 ? 4 : 2;
        bool dark = !string.Equals(args.Theme, "light", StringComparison.OrdinalIgnoreCase);
        return await _export(path, format, scale, dark);
    }

    public sealed record Args
    {
        public string? Path { get; init; }
        public string? Format { get; init; }
        public int? Scale { get; init; }
        public string? Theme { get; init; }
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
        "Read the 3D Cube Viewer state: loaded cube name/object, dimensions, view mode, current channel " +
        "and its spectral value, colormap, stretch, render mode, window levels, unit, and data range.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override Task<CubeViewState?> HandleAsync(Args args, McpToolContext context, CancellationToken ct) => _get();

    public sealed record Args { }
}

/// <summary>
/// <c>probe_cube_spectrum</c> — extract the spectrum (flux vs channel) at a spatial pixel of the cube.
/// </summary>
public sealed class ProbeCubeSpectrumTool : JsonReadTool<ProbeCubeSpectrumTool.Args, CubeSpectrumResult?>
{
    private readonly Func<int, int, Task<CubeSpectrumResult?>> _probe;
    public ProbeCubeSpectrumTool(Func<int, int, Task<CubeSpectrumResult?>> probe) => _probe = probe;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "probe_cube_spectrum",
        "Extract the spectrum (flux per channel, with the spectral-axis world value per channel) at a " +
        "0-based spatial pixel (x, y) of the loaded cube. Returns null if no cube is loaded or (x,y) is out of range.",
        """{"type":"object","properties":{"x":{"type":"integer","minimum":0},"y":{"type":"integer","minimum":0}},"required":["x","y"],"additionalProperties":false}""");

    protected override Task<CubeSpectrumResult?> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (args.X is null || args.Y is null)
            throw new McpToolException(new InvalidArgument("x and y are required"));
        if (args.X < 0 || args.Y < 0)
            throw new McpToolException(new InvalidArgument("x and y must be >= 0"));
        return _probe(args.X.Value, args.Y.Value);
    }

    public sealed record Args { public int? X { get; init; } public int? Y { get; init; } }
}
