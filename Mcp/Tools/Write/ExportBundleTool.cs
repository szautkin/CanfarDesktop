using CanfarDesktop.Services.Export;

namespace CanfarDesktop.Mcp.Tools.Write;

/// <summary>
/// <c>export_research_bundle</c> — assemble the user's research data (downloaded observations + notes +
/// saved/recent searches) into a Claude-friendly bundle (manifest + README + per-module markdown/JSON),
/// zip it under a local folder, and optionally upload the zip to VOSpace. Verb class ViewState: live-applied.
/// </summary>
public sealed class ExportResearchBundleTool : JsonReadTool<ExportResearchBundleTool.Args, ExportResearchBundleTool.Output>
{
    private readonly Func<string, bool, bool, bool, bool, CancellationToken, Task<ExportBundleResult>> _export;

    public ExportResearchBundleTool(Func<string, bool, bool, bool, bool, CancellationToken, Task<ExportBundleResult>> export)
        => _export = export;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "export_research_bundle",
        "Export a Claude-friendly research bundle (manifest + README + per-module markdown/JSON for the user's " +
        "downloaded observations, notes, and saved/recent searches) into a local folder, zipped. Optionally " +
        "upload the zip to VOSpace (Verbinal-Exports/). The destination must be a full path to an EXISTING " +
        "folder. Returns the bundle folder, the zip path, and the VOSpace path if uploaded. Live-applied.",
        """{"type":"object","properties":{"destFolder":{"type":"string","description":"Existing local folder to write the bundle + zip into (full path)"},"includeNotes":{"type":"boolean","description":"Include research notes (default true)"},"includeSearchHistory":{"type":"boolean","description":"Include saved/recent searches (default true)"},"includeFiles":{"type":"boolean","description":"Copy attached local files into the bundle (default false)"},"uploadToVospace":{"type":"boolean","description":"Also upload the zip to VOSpace (default false; needs sign-in)"}},"required":["destFolder"],"additionalProperties":false}""");

    protected override async Task<Output> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var dest = (args.DestFolder ?? string.Empty).Trim();
        if (dest.Length == 0) throw new McpToolException(new InvalidArgument("destFolder is required"));
        if (!Path.IsPathRooted(dest)) throw new McpToolException(new InvalidArgument("destFolder must be a full (rooted) path"));
        string full;
        try { full = Path.GetFullPath(dest); } catch { throw new McpToolException(new InvalidArgument("invalid destFolder")); }
        if (!Directory.Exists(full)) throw new McpToolException(new InvalidArgument($"destFolder does not exist: {full}"));

        ExportBundleResult result;
        try
        {
            result = await _export(
                full,
                args.IncludeNotes ?? true,
                args.IncludeSearchHistory ?? true,
                args.IncludeFiles ?? false,
                args.UploadToVospace ?? false,
                ct);
        }
        catch (ExportException ex)
        {
            throw new McpToolException(new BackendError(ex.Message));
        }

        return new Output(result.BundleDir, result.ZipPath, result.RemotePath);
    }

    public sealed record Args
    {
        public string? DestFolder { get; init; }
        public bool? IncludeNotes { get; init; }
        public bool? IncludeSearchHistory { get; init; }
        public bool? IncludeFiles { get; init; }
        public bool? UploadToVospace { get; init; }
    }

    public sealed record Output(string BundleDir, string ZipPath, string? RemotePath);
}
