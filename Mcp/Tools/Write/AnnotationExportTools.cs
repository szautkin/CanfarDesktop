namespace CanfarDesktop.Mcp.Tools.Write;

/// <summary>What <c>export_annotations</c> was asked for.</summary>
/// <param name="Path">Where to write. The format follows the extension.</param>
/// <param name="Viewer">Which viewer's file the marks are on.</param>
/// <param name="Target">The file, or null for the one on screen.</param>
public sealed record AnnotationExportRequest(string Path, string Viewer, string? Target);

/// <summary>What came of it.</summary>
/// <param name="Marks">How many were written — zero is a real answer, not a failure.</param>
public sealed record AnnotationExportOutcome(
    bool Exported, string? Path, string? Format, int Marks, string? Message);

/// <summary>
/// <c>export_annotations</c> — write the marks on a file out as data.
///
/// <para>Marks were reachable only by drawing them or by listing them through this tool surface. That
/// is enough to talk about them and not enough to USE them: a position without the image it came from
/// is a number nobody can check, and a mark that cannot leave the app cannot go in a paper, a
/// reduction script or a colleague's inbox.</para>
///
/// <para>Two formats, chosen by the extension, because they answer different questions. JSON carries
/// everything — both coordinate systems, sizes in three units, and the observation's publisher id,
/// proposal and release so the image can be fetched again. A DS9 region file carries less and is read
/// by every tool in the field.</para>
///
/// <para>Verb class ViewState: it writes a file the caller named and changes nothing in the app.</para>
/// </summary>
public sealed class ExportAnnotationsTool : JsonReadTool<ExportAnnotationsTool.Args, AnnotationExportOutcome>
{
    private readonly Func<AnnotationExportRequest, Task<AnnotationExportOutcome>> _export;

    public ExportAnnotationsTool(Func<AnnotationExportRequest, Task<AnnotationExportOutcome>> export)
        => _export = export;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "export_annotations",
        "Write the marks on a file out as data, so they can be used outside the app. The format " +
        "follows the path's extension: .json carries everything — each mark's sky position AND its " +
        "pixel, its size in degrees, arcseconds and pixels, its label, who drew it, plus the image " +
        "and, when the app downloaded it, the observation's publisher id, proposal and release date " +
        "so it can be fetched again. .reg writes a DS9 region file, which carries less and is read by " +
        "DS9, CARTA and most reduction scripts; it uses fk5 when the image has WCS so the regions " +
        "land on any image of the same field. Defaults to the file on screen. Live-applied.",
        """
        {"type":"object","properties":{
          "path":{"type":"string","description":"Absolute path ending in .json or .reg."},
          "viewer":{"type":"string","enum":["fits","cube"],"description":"Default fits."},
          "target":{"type":"string","description":"The file whose marks to write. Defaults to the one on screen."}
        },"required":["path"],"additionalProperties":false}
        """);

    protected override async Task<AnnotationExportOutcome> HandleAsync(
        Args args, McpToolContext context, CancellationToken ct)
    {
        var path = (args.Path ?? string.Empty).Trim();
        if (path.Length == 0) throw new McpToolException(new InvalidArgument("path is required"));

        var full = ToolPaths.RequireRootedFullPath(path, "path");
        var extension = System.IO.Path.GetExtension(full).ToLowerInvariant();

        // Refused rather than guessed: the two formats are not interchangeable, and writing DS9
        // regions into a file somebody named .json would be a surprise they find much later.
        if (extension is not (".json" or ".reg"))
            throw new McpToolException(new InvalidArgument("path must end in .json or .reg"));

        var viewer = UpdateAnnotationTool.ParseViewer(args.Viewer);

        return await _export(new AnnotationExportRequest(
            full, AnnotationArgs.Name(viewer), string.IsNullOrWhiteSpace(args.Target) ? null : args.Target!.Trim()));
    }

    public sealed record Args
    {
        public string? Path { get; init; }
        public string? Viewer { get; init; }
        public string? Target { get; init; }
    }
}
