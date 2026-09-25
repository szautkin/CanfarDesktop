namespace CanfarDesktop.Mcp.Tools.Write;

/// <summary>How the caller said which part of the image the figure is of.</summary>
public enum FigureRegionKind
{
    /// <summary>What the viewer is showing — the default, and what the toolbar button exports.</summary>
    View,

    /// <summary>The whole frame, whatever the viewer happens to be zoomed into.</summary>
    Image,

    /// <summary>An explicit box in image pixels.</summary>
    PixelBox,

    /// <summary>A circle on the sky, as the box that contains it.</summary>
    SkyCircle,

    /// <summary>Around a mark someone drew — the figure of "this thing here".</summary>
    Mark,
}

/// <summary>A figure to render. Everything the page needs, with the region already reduced to one of four forms.</summary>
public sealed record FitsFigureRequest(
    string Path,
    string Format,
    int Scale,
    bool Dark,
    bool ShowMarks,
    bool Annotate,
    FigureRegionKind RegionKind,
    double? X = null,
    double? Y = null,
    double? Width = null,
    double? Height = null,
    double? RaDeg = null,
    double? DecDeg = null,
    double? RadiusDeg = null,
    string? MarkId = null);

/// <summary>What was written, and of what.</summary>
public sealed record FitsFigureOutcome(
    bool Exported,
    string? Path,
    string Format,
    int Scale,
    string? Region,
    int Marks,
    string? Message = null)
{
    public static FitsFigureOutcome Unavailable(string message)
        => new(false, null, "png", 1, null, 0, message);
}

/// <summary>
/// <c>export_fits_figure</c> — write a publication figure of what the FITS viewer is showing.
///
/// The figure carries what a reader needs in order to believe it: the region's real sky coordinates and
/// field of view, the cut levels and stretch the picture was made with, a colorbar, and the marks with
/// their labels. That is the difference between a figure and a screenshot, and it is why this tool
/// exists rather than a "take a picture of the window" one.
/// </summary>
public sealed class ExportFitsFigureTool : JsonReadTool<ExportFitsFigureTool.Args, FitsFigureOutcome>
{
    private readonly Func<FitsFigureRequest, Task<FitsFigureOutcome>> _export;

    public ExportFitsFigureTool(Func<FitsFigureRequest, Task<FitsFigureOutcome>> export) => _export = export;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    /// <summary>A 4x plate of a large region is real work; a minute is generous rather than optimistic.</summary>
    protected override TimeSpan Timeout => TimeSpan.FromMinutes(2);

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "export_fits_figure",
        "Save a publication figure of the open FITS image as PNG or PDF: the picture, the marks drawn on " +
        "it with their labels, the cut levels and stretch it was made with, a colorbar, and the region's " +
        "sky coordinates and field of view. Say which part of the image four ways — the view on screen " +
        "(the default), the whole frame, a pixel box, a circle on the sky, or around a mark by its id. " +
        "The picture is rendered from the pixel data at the requested scale, so it is not a screenshot: " +
        "at 2x or 4x the marks and the text scale with it rather than staying screen-sized.",
        """
        {"type":"object","properties":{
          "path":{"type":"string","description":"Absolute path ending in .png or .pdf."},
          "format":{"type":"string","enum":["png","pdf"],"description":"Defaults to the path's extension."},
          "scale":{"type":"integer","enum":[1,2,4],"description":"1, 2 or 4. Default 2."},
          "region":{"type":"string","enum":["view","image","box","sky","mark"],"description":"Default view."},
          "x":{"type":"number","description":"With region=box: left edge in image pixels."},
          "y":{"type":"number","description":"With region=box: top edge in image pixels."},
          "width":{"type":"number","description":"With region=box."},
          "height":{"type":"number","description":"With region=box."},
          "raDeg":{"type":"number","description":"With region=sky: ICRS centre."},
          "decDeg":{"type":"number","description":"With region=sky: ICRS centre."},
          "radiusDeg":{"type":"number","description":"With region=sky: radius in degrees."},
          "markId":{"type":"string","description":"With region=mark: the mark to frame (list_fits_annotations)."},
          "dark":{"type":"boolean","description":"Dark plate (default) or a light one for a journal."},
          "marks":{"type":"boolean","description":"Draw the marks on the figure. Default true."},
          "annotate":{"type":"boolean","description":"Header and footer. Default true; false gives the bare picture."}
        },"required":["path"],"additionalProperties":false}
        """);

    protected override async Task<FitsFigureOutcome> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var path = (args.Path ?? string.Empty).Trim();
        if (path.Length == 0) throw new McpToolException(new InvalidArgument("path is required"));
        if (!System.IO.Path.IsPathRooted(path)) throw new McpToolException(new InvalidArgument("path must be absolute"));

        var extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
        var format = (args.Format ?? string.Empty).Trim().ToLowerInvariant();
        if (format.Length == 0)
        {
            // Taken from the path when unsaid: a caller who wrote ".pdf" has already told us.
            format = extension == ".pdf" ? "pdf" : "png";
        }
        if (format is not ("png" or "pdf"))
            throw new McpToolException(new InvalidArgument("format must be 'png' or 'pdf'"));
        if (extension != "." + format)
            throw new McpToolException(new InvalidArgument($"path must end in .{format} for a {format.ToUpperInvariant()} export"));

        // 1, 2 or 4 — the three the export dialog offers. A figure an agent made at 3x would be one
        // nobody could reproduce by hand, and a figure that cannot be remade is a figure with no method.
        var scale = args.Scale ?? 2;
        if (scale is not (1 or 2 or 4))
            throw new McpToolException(new InvalidArgument("scale must be 1, 2 or 4"));

        var kind = ParseRegion(args.Region);
        ValidateRegionArgs(kind, args);

        return await _export(new FitsFigureRequest(
            path, format, scale,
            Dark: args.Dark ?? true,
            ShowMarks: args.Marks ?? true,
            Annotate: args.Annotate ?? true,
            kind,
            args.X, args.Y, args.Width, args.Height,
            args.RaDeg, args.DecDeg, args.RadiusDeg,
            args.MarkId?.Trim()));
    }

    private static FigureRegionKind ParseRegion(string? text) => (text ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "" or "view" or "current" => FigureRegionKind.View,
        "image" or "full" or "frame" => FigureRegionKind.Image,
        "box" or "pixels" or "pixelbox" => FigureRegionKind.PixelBox,
        "sky" or "circle" or "cone" => FigureRegionKind.SkyCircle,
        "mark" or "annotation" => FigureRegionKind.Mark,
        _ => throw new McpToolException(new InvalidArgument(
            $"region must be one of: view, image, box, sky, mark — got '{text}'")),
    };

    /// <summary>
    /// Each region form needs its own arguments, and saying "box" without a box is a mistake worth
    /// naming rather than quietly falling back to the current view — which would export a figure of
    /// something other than what was asked for, and look like it had worked.
    /// </summary>
    private static void ValidateRegionArgs(FigureRegionKind kind, Args args)
    {
        switch (kind)
        {
            case FigureRegionKind.PixelBox:
                if (args.X is null || args.Y is null || args.Width is null || args.Height is null)
                    throw new McpToolException(new InvalidArgument("region=box needs x, y, width and height"));
                if (args.Width <= 0 || args.Height <= 0)
                    throw new McpToolException(new InvalidArgument("width and height must be greater than zero"));
                break;

            case FigureRegionKind.SkyCircle:
                if (args.RaDeg is null || args.DecDeg is null || args.RadiusDeg is null)
                    throw new McpToolException(new InvalidArgument("region=sky needs raDeg, decDeg and radiusDeg"));
                if (args.RaDeg is < 0 or > 360)
                    throw new McpToolException(new InvalidArgument("raDeg must be in [0, 360]"));
                if (args.DecDeg is < -90 or > 90)
                    throw new McpToolException(new InvalidArgument("decDeg must be in [-90, 90]"));
                if (args.RadiusDeg <= 0)
                    throw new McpToolException(new InvalidArgument("radiusDeg must be greater than zero"));
                break;

            case FigureRegionKind.Mark:
                if (string.IsNullOrWhiteSpace(args.MarkId))
                    throw new McpToolException(new InvalidArgument(
                        "region=mark needs markId — list_fits_annotations gives the ids"));
                break;
        }
    }

    public sealed record Args
    {
        public string? Path { get; init; }
        public string? Format { get; init; }
        public int? Scale { get; init; }
        public string? Region { get; init; }
        public double? X { get; init; }
        public double? Y { get; init; }
        public double? Width { get; init; }
        public double? Height { get; init; }
        public double? RaDeg { get; init; }
        public double? DecDeg { get; init; }
        public double? RadiusDeg { get; init; }
        public string? MarkId { get; init; }
        public bool? Dark { get; init; }
        public bool? Marks { get; init; }
        public bool? Annotate { get; init; }
    }
}
