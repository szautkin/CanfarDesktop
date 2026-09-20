using System.Globalization;
using CanfarDesktop.Models;
using CanfarDesktop.Services.Fits;

namespace CanfarDesktop.Mcp.Tools.Write;

/// <summary>
/// Shared work for the annotate_* tools: resolving which file a mark belongs to, turning arguments into
/// a mark, and refusing the ones that cannot be drawn.
///
/// One place, because two viewers ask the same questions in the same order and a mark that is legal on
/// one canvas and not the other would be a difference nobody could explain.
/// </summary>
public static class AnnotationArgs
{
    /// <summary>The style fields every annotate/update tool accepts, as JSON-Schema properties.</summary>
    public const string StyleSchema =
        """
        "colour":{"type":"string","description":"Ink as #rrggbb. Omit to use the author's default."},
        "fontSize":{"type":"number","description":"Label size in device pixels (6-72)."},
        "bold":{"type":"boolean"},
        "stroke":{"type":"number","description":"Outline width in device pixels (0.5-20)."}
        """;

    /// <summary>
    /// A style from the given fields, over a base. Every field is optional: a caller changing only the
    /// colour must not have the weight reset under it.
    /// </summary>
    public static MarkStyle StyleFrom(MarkStyle basis, string? colour, double? fontSize, bool? bold, double? stroke)
    {
        var style = basis;

        if (!string.IsNullOrWhiteSpace(colour))
        {
            if (MarkStyle.ColourFromHex(colour) is not { } rgb)
                throw new McpToolException(new InvalidArgument(
                    $"'{colour}' is not a colour — give it as #rrggbb (a typo that silently drew black would be an invisible mark)"));
            style = style with { Red = rgb.R, Green = rgb.G, Blue = rgb.B };
        }

        if (fontSize is { } f) style = style with { FontSize = f };
        if (bold is { } b) style = style with { Bold = b };
        if (stroke is { } s) style = style with { Stroke = s };

        return style.Sane();
    }

    /// <summary>Whether any style field was given at all — nothing said means "however this author's marks are drawn".</summary>
    public static bool AnyStyleGiven(string? colour, double? fontSize, bool? bold, double? stroke)
        => !string.IsNullOrWhiteSpace(colour) || fontSize is not null || bold is not null || stroke is not null;

    /// <summary>An id that is stable, short enough to type back, and unique within a target.</summary>
    public static string NewId() => "m" + Guid.NewGuid().ToString("N")[..8];

    public static string Now() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    /// <summary>The extent a radius or a width/height pair asks for, or null when neither was given.</summary>
    public static Extent? ExtentFrom(double? radius, double? halfWidth, double? halfHeight)
    {
        if (halfWidth is { } w && halfHeight is { } h) return new Extent(w, h);
        if (radius is { } r) return Extent.Square(r);
        if (halfWidth is { } only) return Extent.Square(only);
        if (halfHeight is { } onlyH) return Extent.Square(onlyH);
        return null;
    }

    /// <summary>
    /// Store the mark and tell the viewer. The viewer showing another file is not a failure: marks
    /// belong to the file, so they are saved and will be there when it is opened.
    /// </summary>
    public static async Task<AnnotationChange> ApplyAsync(
        IAnnotationStore store, IAnnotationHost host, AnnotationViewer viewer, string target, Annotation mark)
    {
        if (mark.Validate() is { } wrong)
            throw new McpToolException(new InvalidArgument(wrong));

        IReadOnlyList<Annotation> after;
        try
        {
            after = store.Add(target, mark);
        }
        catch (Exception ex)
        {
            throw new McpToolException(new BackendError(ex.Message));
        }

        var shown = await host.RefreshAsync(viewer, target, mark.Id);
        return new AnnotationChange(true, Name(viewer), target, shown, AnnotationView.From(mark), after.Count,
            shown ? null : "saved against the file; the viewer is not showing it at the moment");
    }

    public static string Name(AnnotationViewer viewer) => viewer == AnnotationViewer.Cube ? "cube" : "fits";

    /// <summary>
    /// The file the mark is for: the one named, or whatever the viewer has open. Named explicitly, a
    /// caller can annotate a file that is not on screen — which is how a batch of marks gets prepared
    /// before anyone looks at it.
    /// </summary>
    public static async Task<string?> ResolveTargetAsync(IAnnotationHost host, AnnotationViewer viewer, string? given)
        => string.IsNullOrWhiteSpace(given) ? await host.ActiveTargetAsync(viewer) : given.Trim();
}

/// <summary><c>annotate_fits</c> — draw a mark on the FITS viewer's image.</summary>
public sealed class AnnotateFitsTool : JsonReadTool<AnnotateFitsTool.Args, AnnotationChange>
{
    private readonly IAnnotationStore _store;
    private readonly IAnnotationHost _host;

    public AnnotateFitsTool(IAnnotationStore store, IAnnotationHost host)
    {
        _store = store;
        _host = host;
    }

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "annotate_fits",
        "Draw a mark on a FITS image: a circle or box around a source, a callout with a leader line to a " +
        "label, or a bare label. Give the position EITHER in image pixels (x, y) or on the sky " +
        "(raDeg, decDeg) — prefer the sky when the file has WCS, because a sky mark points at the same " +
        "place in a different image of the same field. Sizes are in the position's own units, so a mark " +
        "keeps its size on the subject as the view zooms. The mark persists with the file, and is " +
        "labelled as an agent's.",
        $$"""
        {"type":"object","properties":{
          "kind":{"type":"string","enum":["circle","rect","callout","text"],"description":"Default circle."},
          "x":{"type":"number","description":"Image pixel X (with y)."},
          "y":{"type":"number","description":"Image pixel Y (with x)."},
          "raDeg":{"type":"number","description":"ICRS Right Ascension in degrees (with decDeg)."},
          "decDeg":{"type":"number","description":"ICRS Declination in degrees (with raDeg)."},
          "radius":{"type":"number","description":"Half-size, in the position's own units."},
          "halfWidth":{"type":"number"},
          "halfHeight":{"type":"number"},
          "text":{"type":"string","description":"The label. Required for a callout or a text mark."},
          "labelOffsetX":{"type":"number","description":"Where a callout's label sits, in screen pixels from the anchor."},
          "labelOffsetY":{"type":"number"},
          "target":{"type":"string","description":"File to annotate. Defaults to the image on screen."},
          {{AnnotationArgs.StyleSchema}}
        },"additionalProperties":false}
        """);

    protected override async Task<AnnotationChange> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var target = await AnnotationArgs.ResolveTargetAsync(_host, AnnotationViewer.Fits, args.Target);
        if (target is null)
            return AnnotationChange.NothingOpen("fits", "no FITS file is open — open one, or name a `target` file");

        var anchor = BuildAnchor(args);
        var kind = AnnotationKindExtensions.Parse(args.Kind) ?? AnnotationKind.Circle;

        var mark = new Annotation
        {
            Id = AnnotationArgs.NewId(),
            Kind = kind,
            Anchor = anchor,
            Extent = AnnotationArgs.ExtentFrom(args.Radius, args.HalfWidth, args.HalfHeight)
                     ?? (kind.NeedsExtent() ? DefaultExtent(anchor) : null),
            Text = args.Text ?? string.Empty,
            LabelOffsetX = args.LabelOffsetX,
            LabelOffsetY = args.LabelOffsetY,
            Author = MarkAuthor.Agent,
            Style = AnnotationArgs.AnyStyleGiven(args.Colour, args.FontSize, args.Bold, args.Stroke)
                ? AnnotationArgs.StyleFrom(MarkStyle.AgentDefault, args.Colour, args.FontSize, args.Bold, args.Stroke)
                : null,
            CreatedAt = AnnotationArgs.Now(),
        };

        return await AnnotationArgs.ApplyAsync(_store, _host, AnnotationViewer.Fits, target, mark);
    }

    /// <summary>
    /// A shape with no size given still gets one. Refusing would be defensible, but the size that is
    /// wanted nine times in ten is "big enough to see around a source", and in sky units that is not a
    /// number a caller can be expected to guess.
    /// </summary>
    private static Extent DefaultExtent(AnnotationAnchor anchor)
        => Extent.Square(anchor.Space == AnchorSpace.Sky ? 0.002 : 20);   // ~7 arcsec, or 20 pixels

    private static AnnotationAnchor BuildAnchor(Args args)
    {
        var hasPixel = args.X is not null && args.Y is not null;
        var hasSky = args.RaDeg is not null && args.DecDeg is not null;

        if (hasPixel && hasSky)
            throw new McpToolException(new InvalidArgument(
                "give the position once: either x and y, or raDeg and decDeg"));

        if (hasSky) return AnnotationAnchor.Sky(args.RaDeg!.Value, args.DecDeg!.Value);
        if (hasPixel) return AnnotationAnchor.ImagePixel(args.X!.Value, args.Y!.Value);

        throw new McpToolException(new InvalidArgument(
            "a mark needs a position: x and y (image pixels), or raDeg and decDeg (sky)"));
    }

    public sealed record Args
    {
        public string? Kind { get; init; }
        public double? X { get; init; }
        public double? Y { get; init; }
        public double? RaDeg { get; init; }
        public double? DecDeg { get; init; }
        public double? Radius { get; init; }
        public double? HalfWidth { get; init; }
        public double? HalfHeight { get; init; }
        public string? Text { get; init; }
        public double? LabelOffsetX { get; init; }
        public double? LabelOffsetY { get; init; }
        public string? Target { get; init; }
        public string? Colour { get; init; }
        public double? FontSize { get; init; }
        public bool? Bold { get; init; }
        public double? Stroke { get; init; }
    }
}

/// <summary><c>annotate_cube</c> — draw a mark in the cube viewer, on a channel.</summary>
public sealed class AnnotateCubeTool : JsonReadTool<AnnotateCubeTool.Args, AnnotationChange>
{
    private readonly IAnnotationStore _store;
    private readonly IAnnotationHost _host;

    public AnnotateCubeTool(IAnnotationStore store, IAnnotationHost host)
    {
        _store = store;
        _host = host;
    }

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "annotate_cube",
        "Draw a mark in the cube viewer. A cube mark lives on a CHANNEL: it is drawn on the slice showing " +
        "that channel and in the volume, not on every slice. Positions are voxels (x, y, channel), and a " +
        "circle is a sphere — so it projects to an ellipse when the camera is off-axis. The mark " +
        "persists with the file, and is labelled as an agent's.",
        $$"""
        {"type":"object","properties":{
          "kind":{"type":"string","enum":["circle","rect","callout","text"],"description":"Default circle."},
          "x":{"type":"number"},
          "y":{"type":"number"},
          "channel":{"type":"integer","minimum":0,"description":"The channel the mark lives on."},
          "radius":{"type":"number","description":"Half-size in voxels."},
          "halfWidth":{"type":"number"},
          "halfHeight":{"type":"number"},
          "text":{"type":"string","description":"The label. Required for a callout or a text mark."},
          "labelOffsetX":{"type":"number"},
          "labelOffsetY":{"type":"number"},
          "target":{"type":"string","description":"File to annotate. Defaults to the cube on screen."},
          {{AnnotationArgs.StyleSchema}}
        },"required":["x","y","channel"],"additionalProperties":false}
        """);

    protected override async Task<AnnotationChange> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var target = await AnnotationArgs.ResolveTargetAsync(_host, AnnotationViewer.Cube, args.Target);
        if (target is null)
            return AnnotationChange.NothingOpen("cube", "no cube is open — open one, or name a `target` file");

        if (args.X is null || args.Y is null || args.Channel is null)
            throw new McpToolException(new InvalidArgument("a cube mark needs x, y and channel"));

        var anchor = AnnotationAnchor.Data(args.X.Value, args.Y.Value, args.Channel.Value);
        var kind = AnnotationKindExtensions.Parse(args.Kind) ?? AnnotationKind.Circle;

        var mark = new Annotation
        {
            Id = AnnotationArgs.NewId(),
            Kind = kind,
            Anchor = anchor,
            Extent = AnnotationArgs.ExtentFrom(args.Radius, args.HalfWidth, args.HalfHeight)
                     ?? (kind.NeedsExtent() ? Extent.Square(10) : null),
            Text = args.Text ?? string.Empty,
            LabelOffsetX = args.LabelOffsetX,
            LabelOffsetY = args.LabelOffsetY,
            Author = MarkAuthor.Agent,
            Style = AnnotationArgs.AnyStyleGiven(args.Colour, args.FontSize, args.Bold, args.Stroke)
                ? AnnotationArgs.StyleFrom(MarkStyle.AgentDefault, args.Colour, args.FontSize, args.Bold, args.Stroke)
                : null,
            CreatedAt = AnnotationArgs.Now(),
        };

        return await AnnotationArgs.ApplyAsync(_store, _host, AnnotationViewer.Cube, target, mark);
    }

    public sealed record Args
    {
        public string? Kind { get; init; }
        public double? X { get; init; }
        public double? Y { get; init; }
        public int? Channel { get; init; }
        public double? Radius { get; init; }
        public double? HalfWidth { get; init; }
        public double? HalfHeight { get; init; }
        public string? Text { get; init; }
        public double? LabelOffsetX { get; init; }
        public double? LabelOffsetY { get; init; }
        public string? Target { get; init; }
        public string? Colour { get; init; }
        public double? FontSize { get; init; }
        public bool? Bold { get; init; }
        public double? Stroke { get; init; }
    }
}

/// <summary>Listing the marks on a viewer's file. One implementation; the two tools differ by viewer.</summary>
public abstract class ListAnnotationsToolBase : JsonReadTool<ListAnnotationsToolBase.Args, AnnotationListView>
{
    private readonly IAnnotationStore _store;
    private readonly IAnnotationHost _host;

    protected ListAnnotationsToolBase(IAnnotationStore store, IAnnotationHost host)
    {
        _store = store;
        _host = host;
    }

    protected abstract AnnotationViewer Viewer { get; }

    public override McpVerbClass VerbClass => McpVerbClass.Read;

    protected override async Task<AnnotationListView> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var name = AnnotationArgs.Name(Viewer);
        var target = await AnnotationArgs.ResolveTargetAsync(_host, Viewer, args.Target);
        if (target is null)
            return AnnotationListView.NothingOpen(name, $"nothing is open in the {name} viewer — open a file, or name a `target`");

        var marks = _store.LoadFor(target);
        var shown = string.Equals(await _host.ActiveTargetAsync(Viewer), target, StringComparison.OrdinalIgnoreCase);

        return new AnnotationListView(name, target, shown, marks.Count,
            marks.Select(AnnotationView.From).ToList(),
            marks.Count == 0 ? "nothing has been drawn on this file" : null);
    }

    public sealed record Args { public string? Target { get; init; } }
}

/// <summary><c>list_fits_annotations</c> — the marks on a FITS file.</summary>
public sealed class ListFitsAnnotationsTool : ListAnnotationsToolBase
{
    public ListFitsAnnotationsTool(IAnnotationStore store, IAnnotationHost host) : base(store, host) { }

    protected override AnnotationViewer Viewer => AnnotationViewer.Fits;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "list_fits_annotations",
        "The marks drawn on a FITS file — their ids, kinds, positions, sizes, labels, colours, and " +
        "whether a person or an agent drew each one. Defaults to the image on screen. Use the ids with " +
        "update_annotation, remove_annotation and select_annotation.",
        """{"type":"object","properties":{"target":{"type":"string"}},"additionalProperties":false}""");
}

/// <summary><c>list_cube_annotations</c> — the marks in a cube.</summary>
public sealed class ListCubeAnnotationsTool : ListAnnotationsToolBase
{
    public ListCubeAnnotationsTool(IAnnotationStore store, IAnnotationHost host) : base(store, host) { }

    protected override AnnotationViewer Viewer => AnnotationViewer.Cube;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "list_cube_annotations",
        "The marks drawn in a cube — including the channel each one lives on. Defaults to the cube on " +
        "screen. Use the ids with update_annotation, remove_annotation and select_annotation.",
        """{"type":"object","properties":{"target":{"type":"string"}},"additionalProperties":false}""");
}

/// <summary><c>update_annotation</c> — change a mark that is already there.</summary>
public sealed class UpdateAnnotationTool : JsonReadTool<UpdateAnnotationTool.Args, AnnotationChange>
{
    private readonly IAnnotationStore _store;
    private readonly IAnnotationHost _host;

    public UpdateAnnotationTool(IAnnotationStore store, IAnnotationHost host)
    {
        _store = store;
        _host = host;
    }

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "update_annotation",
        "Change a mark: move it, resize it, relabel it, or restyle it. Send only what you are changing — " +
        "everything else is left as it is. The id comes from list_fits_annotations or " +
        "list_cube_annotations.",
        $$"""
        {"type":"object","properties":{
          "id":{"type":"string"},
          "viewer":{"type":"string","enum":["fits","cube"],"description":"Which viewer's file the mark is on. Default fits."},
          "target":{"type":"string","description":"Defaults to the file on screen in that viewer."},
          "x":{"type":"number"},
          "y":{"type":"number"},
          "raDeg":{"type":"number"},
          "decDeg":{"type":"number"},
          "channel":{"type":"integer","minimum":0},
          "radius":{"type":"number"},
          "halfWidth":{"type":"number"},
          "halfHeight":{"type":"number"},
          "text":{"type":"string"},
          "labelOffsetX":{"type":"number"},
          "labelOffsetY":{"type":"number"},
          {{AnnotationArgs.StyleSchema}}
        },"required":["id"],"additionalProperties":false}
        """);

    protected override async Task<AnnotationChange> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var id = (args.Id ?? string.Empty).Trim();
        if (id.Length == 0) throw new McpToolException(new InvalidArgument("id is required"));

        var viewer = ParseViewer(args.Viewer);
        var name = AnnotationArgs.Name(viewer);

        var target = await AnnotationArgs.ResolveTargetAsync(_host, viewer, args.Target);
        if (target is null)
            return AnnotationChange.NothingOpen(name, $"nothing is open in the {name} viewer — open a file, or name a `target`");

        var existing = _store.LoadFor(target).FirstOrDefault(a => a.Id == id);
        if (existing is null)
            throw new McpToolException(new UnknownTarget(
                $"no mark '{id}' on {target}. Use list_{name}_annotations to see what is there."));

        var updated = existing with
        {
            Anchor = MovedAnchor(existing.Anchor, args),
            Extent = AnnotationArgs.ExtentFrom(args.Radius, args.HalfWidth, args.HalfHeight) ?? existing.Extent,
            Text = args.Text ?? existing.Text,
            LabelOffsetX = args.LabelOffsetX ?? existing.LabelOffsetX,
            LabelOffsetY = args.LabelOffsetY ?? existing.LabelOffsetY,
            Style = AnnotationArgs.AnyStyleGiven(args.Colour, args.FontSize, args.Bold, args.Stroke)
                // Over the mark's CURRENT appearance, not over a default: a caller changing only the
                // colour must not have the weight reset under it.
                ? AnnotationArgs.StyleFrom(existing.EffectiveStyle, args.Colour, args.FontSize, args.Bold, args.Stroke)
                : existing.Style,
        };

        if (updated.Validate() is { } wrong)
            throw new McpToolException(new InvalidArgument(wrong));

        IReadOnlyList<Annotation>? after;
        try
        {
            after = _store.Update(target, updated);
        }
        catch (Exception ex)
        {
            throw new McpToolException(new BackendError(ex.Message));
        }

        if (after is null)
            throw new McpToolException(new UnknownTarget($"no mark '{id}' on {target}"));

        var shown = await _host.RefreshAsync(viewer, target, updated.Id);
        return new AnnotationChange(true, name, target, shown, AnnotationView.From(updated), after.Count,
            shown ? null : "saved against the file; the viewer is not showing it at the moment");
    }

    /// <summary>The anchor after the move, keeping its space unless a different one was given.</summary>
    private static AnnotationAnchor MovedAnchor(AnnotationAnchor current, Args args)
    {
        if (args.RaDeg is { } ra && args.DecDeg is { } dec) return AnnotationAnchor.Sky(ra, dec);

        if (args.X is null && args.Y is null && args.Channel is null) return current;

        var x = args.X ?? current.X;
        var y = args.Y ?? current.Y;
        var z = args.Channel ?? current.Z;

        return current.Space switch
        {
            AnchorSpace.Data => AnnotationAnchor.Data(x, y, z),
            AnchorSpace.Sky => AnnotationAnchor.Sky(x, y),
            _ => AnnotationAnchor.ImagePixel(x, y),
        };
    }

    internal static AnnotationViewer ParseViewer(string? text) => (text ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "" or "fits" or "image" => AnnotationViewer.Fits,
        "cube" or "volume" => AnnotationViewer.Cube,
        _ => throw new McpToolException(new InvalidArgument($"viewer must be 'fits' or 'cube' — got '{text}'")),
    };

    public sealed record Args
    {
        public string? Id { get; init; }
        public string? Viewer { get; init; }
        public string? Target { get; init; }
        public double? X { get; init; }
        public double? Y { get; init; }
        public double? RaDeg { get; init; }
        public double? DecDeg { get; init; }
        public int? Channel { get; init; }
        public double? Radius { get; init; }
        public double? HalfWidth { get; init; }
        public double? HalfHeight { get; init; }
        public string? Text { get; init; }
        public double? LabelOffsetX { get; init; }
        public double? LabelOffsetY { get; init; }
        public string? Colour { get; init; }
        public double? FontSize { get; init; }
        public bool? Bold { get; init; }
        public double? Stroke { get; init; }
    }
}

/// <summary><c>remove_annotation</c> — take a mark off.</summary>
public sealed class RemoveAnnotationTool : JsonReadTool<RemoveAnnotationTool.Args, AnnotationChange>
{
    private readonly IAnnotationStore _store;
    private readonly IAnnotationHost _host;

    public RemoveAnnotationTool(IAnnotationStore store, IAnnotationHost host)
    {
        _store = store;
        _host = host;
    }

    /// <summary>Destructive: a drawing removed is not one an undo brings back from here.</summary>
    public override McpVerbClass VerbClass => McpVerbClass.Destructive;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "remove_annotation",
        "Remove one mark by its id. The mark is gone from the file it was drawn on. Use " +
        "list_fits_annotations or list_cube_annotations first if you are not certain which id is which.",
        """
        {"type":"object","properties":{
          "id":{"type":"string"},
          "viewer":{"type":"string","enum":["fits","cube"],"description":"Default fits."},
          "target":{"type":"string","description":"Defaults to the file on screen in that viewer."}
        },"required":["id"],"additionalProperties":false}
        """);

    protected override async Task<AnnotationChange> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var id = (args.Id ?? string.Empty).Trim();
        if (id.Length == 0) throw new McpToolException(new InvalidArgument("id is required"));

        var viewer = UpdateAnnotationTool.ParseViewer(args.Viewer);
        var name = AnnotationArgs.Name(viewer);

        var target = await AnnotationArgs.ResolveTargetAsync(_host, viewer, args.Target);
        if (target is null)
            return AnnotationChange.NothingOpen(name, $"nothing is open in the {name} viewer — open a file, or name a `target`");

        bool removed;
        try
        {
            removed = _store.Remove(target, id);
        }
        catch (Exception ex)
        {
            throw new McpToolException(new BackendError(ex.Message));
        }

        if (!removed)
            throw new McpToolException(new UnknownTarget(
                $"no mark '{id}' on {target}. Use list_{name}_annotations to see what is there."));

        var remaining = _store.LoadFor(target).Count;
        var shown = await _host.RefreshAsync(viewer, target, null);

        return new AnnotationChange(true, name, target, shown, null, remaining);
    }

    public sealed record Args
    {
        public string? Id { get; init; }
        public string? Viewer { get; init; }
        public string? Target { get; init; }
    }
}

/// <summary><c>select_annotation</c> — point at a mark on the user's screen.</summary>
public sealed class SelectAnnotationTool : JsonReadTool<SelectAnnotationTool.Args, AnnotationChange>
{
    private readonly IAnnotationStore _store;
    private readonly IAnnotationHost _host;

    public SelectAnnotationTool(IAnnotationStore store, IAnnotationHost host)
    {
        _store = store;
        _host = host;
    }

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "select_annotation",
        "Pick out a mark on the user's screen — the way you would point at it. Nothing is changed; the " +
        "mark is highlighted in the viewer that is showing its file. Use this to refer to a specific " +
        "mark while talking about it, rather than describing where on the image it is. OMIT id to stop " +
        "pointing and leave nothing picked out — the same as clicking the mark again, or clicking away " +
        "from it, in the viewer.",
        """
        {"type":"object","properties":{
          "id":{"type":"string","description":"The mark to point at. Omit to let go of whatever is picked out."},
          "viewer":{"type":"string","enum":["fits","cube"],"description":"Default fits."}
        },"additionalProperties":false}
        """);

    protected override async Task<AnnotationChange> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var id = (args.Id ?? string.Empty).Trim();

        var viewer = UpdateAnnotationTool.ParseViewer(args.Viewer);
        var name = AnnotationArgs.Name(viewer);

        var target = await _host.ActiveTargetAsync(viewer);
        if (target is null)
            return AnnotationChange.NothingOpen(name, $"nothing is open in the {name} viewer to point at");

        // No id: stop pointing. The gesture a person makes by clicking the mark again, so an agent
        // that picked something out can put it down without having to name it a second time.
        if (id.Length == 0)
        {
            var cleared = await _host.DeselectAsync(viewer, target);
            return new AnnotationChange(cleared, name, target, cleared, null,
                _store.LoadFor(target).Count,
                cleared ? null : "the viewer did not take the change");
        }

        var mark = _store.LoadFor(target).FirstOrDefault(a => a.Id == id);
        if (mark is null)
            throw new McpToolException(new UnknownTarget(
                $"no mark '{id}' on the {name} viewer's current file. Use list_{name}_annotations to see what is there."));

        var shown = await _host.RefreshAsync(viewer, target, id);
        return new AnnotationChange(shown, name, target, shown, AnnotationView.From(mark),
            _store.LoadFor(target).Count,
            shown ? null : "the viewer did not take the selection");
    }

    public sealed record Args
    {
        public string? Id { get; init; }
        public string? Viewer { get; init; }
    }
}

/// <summary>
/// <c>clear_annotations</c> — take every mark off one file.
///
/// Removing them one id at a time worked, and needed a listing first and a call per mark; an agent
/// tidying up after a run it had drawn a dozen marks in spent a dozen round trips on it. The count it
/// answers with is what was actually removed, so "clear" on a file with nothing on it reports zero
/// rather than claiming work it did not do.
/// </summary>
public sealed class ClearAnnotationsTool : JsonReadTool<ClearAnnotationsTool.Args, AnnotationChange>
{
    private readonly IAnnotationStore _store;
    private readonly IAnnotationHost _host;

    public ClearAnnotationsTool(IAnnotationStore store, IAnnotationHost host)
    {
        _store = store;
        _host = host;
    }

    /// <summary>Destructive, and more so than remove_annotation: nothing here comes back.</summary>
    public override McpVerbClass VerbClass => McpVerbClass.Destructive;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "clear_annotations",
        "Remove EVERY mark from one file. There is no undo — use remove_annotation when you mean one of " +
        "them. Defaults to the file on screen in the named viewer; pass `target` to clear a file that is " +
        "not open. Answers with how many were removed.",
        """
        {"type":"object","properties":{
          "viewer":{"type":"string","enum":["fits","cube"],"description":"Default fits."},
          "target":{"type":"string","description":"Defaults to the file on screen in that viewer."}
        },"additionalProperties":false}
        """);

    protected override async Task<AnnotationChange> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var viewer = UpdateAnnotationTool.ParseViewer(args.Viewer);
        var name = AnnotationArgs.Name(viewer);

        var target = await AnnotationArgs.ResolveTargetAsync(_host, viewer, args.Target);
        if (target is null)
            return AnnotationChange.NothingOpen(name, $"nothing is open in the {name} viewer — open a file, or name a `target`");

        int removed;
        try
        {
            removed = _store.LoadFor(target).Count;
            _store.SaveFor(target, []);
        }
        catch (Exception ex)
        {
            throw new McpToolException(new BackendError(ex.Message));
        }

        var shown = await _host.RefreshAsync(viewer, target, null);
        return new AnnotationChange(true, name, target, shown, null, 0, Removed: removed);
    }

    public sealed record Args
    {
        public string? Viewer { get; init; }
        public string? Target { get; init; }
    }
}
