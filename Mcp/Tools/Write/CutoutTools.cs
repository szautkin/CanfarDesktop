using CanfarDesktop.Mcp.Tools.Proposals;
using CanfarDesktop.Models.Cutouts;
using CanfarDesktop.Services.Cutouts;

namespace CanfarDesktop.Mcp.Tools.Write;

// ─────────────────────────────────────────────────────────────────────────────
// Cutouts for agents: what a file can be cut by (get_cutout_options), a cutout proposed as a
// download (download_cutout), and the editor opened on the person's screen (show_cutout_editor).
// Every one of them reads the same arguments into the same CutoutSpec and judges it with the same
// ICutoutSource.Check the editor shows — no second idea of what a valid cutout is, and nothing here
// that knows which way a file is cut.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>A cutout request as an agent writes it: a region (circle, box or polygon, degrees) and optionally a band (metres).</summary>
public sealed record CutoutArgs
{
    public string? PublisherId { get; init; }

    /// <summary>Which file (its SODA id, from get_cutout_options); optional when the observation has one.</summary>
    public string? ArtifactId { get; init; }

    public CircleArg? Circle { get; init; }
    public BoxArg? Box { get; init; }
    public IReadOnlyList<double[]>? Polygon { get; init; }
    public double? BandMin { get; init; }
    public double? BandMax { get; init; }

    /// <summary>Who cuts it: Soda (on CADC's side) or Local (from the file on this computer); the best available when left out.</summary>
    public CutoutMethod? CutBy { get; init; }

    /// <summary>Which images of a multi-extension file to keep ("SCI,1"); every image the region falls on when left out.</summary>
    public IReadOnlyList<string>? Extensions { get; init; }

    public sealed record CircleArg { public double Ra { get; init; } public double Dec { get; init; } public double Radius { get; init; } }
    public sealed record BoxArg { public double Ra { get; init; } public double Dec { get; init; } public double Width { get; init; } public double Height { get; init; } }

    /// <summary>The JSON Schema for these properties, shared by the tools that take them.</summary>
    public const string SchemaProperties =
        """
        "publisherId":{"type":"string","description":"The observation (a search result's publisher_id)."},
        "artifactId":{"type":"string","description":"Which file, as get_cutout_options names it (e.g. cadc:CFHTSG/….fits). Optional when only one file can be cut."},
        "circle":{"type":"object","description":"Everything within radius of a point. Degrees.","properties":{"ra":{"type":"number"},"dec":{"type":"number"},"radius":{"type":"number","exclusiveMinimum":0}},"required":["ra","dec","radius"],"additionalProperties":false},
        "box":{"type":"object","description":"A rectangle on the sky about a point, width along RA and height along Dec. Degrees.","properties":{"ra":{"type":"number"},"dec":{"type":"number"},"width":{"type":"number","exclusiveMinimum":0},"height":{"type":"number","exclusiveMinimum":0}},"required":["ra","dec","width","height"],"additionalProperties":false},
        "polygon":{"type":"array","description":"Corners as [ra, dec] pairs in degrees, at least three.","items":{"type":"array","items":{"type":"number"},"minItems":2,"maxItems":2}},
        "bandMin":{"type":"number","description":"Shortest wavelength to keep, METRES (5e-7 is 500 nm). Only for files that can be cut by wavelength."},
        "bandMax":{"type":"number","description":"Longest wavelength to keep, metres."},
        "extensions":{"type":"array","items":{"type":"string"},"description":"Which images of a multi-extension file to keep, by the names get_cutout_options lists (e.g. \"SCI,1\", \"ERR,1\"); left out, every image the region falls on. Only a local cut can choose."},
        "cutBy":{"type":"string","enum":["soda","local"],"description":"Who cuts it: 'soda' on CADC's side (only the part is downloaded), or 'local' from the observation's file already on this computer (instant, offline; the only way for files CADC will not cut, such as its HST mirror's). Left out: local when the file is here and can be cut, else soda."}
        """;

    /// <summary>The region asked for, or null when none was; throws when more than one was.</summary>
    public SkyRegion? Region()
    {
        var given = (Circle is null ? 0 : 1) + (Box is null ? 0 : 1) + (Polygon is null ? 0 : 1);
        if (given > 1) throw new McpToolException(new InvalidArgument("give one region: circle, box OR polygon"));
        if (Circle is { } c) return SkyRegion.Circle(c.Ra, c.Dec, c.Radius);
        if (Box is { } b) return SkyRegion.Box(b.Ra, b.Dec, b.Width, b.Height);
        if (Polygon is { } p)
        {
            if (p.Any(v => v is not { Length: 2 }))
                throw new McpToolException(new InvalidArgument("polygon corners are [ra, dec] pairs"));
            return SkyRegion.Polygon(p.Select(v => new SkyPoint(v[0], v[1])));
        }
        return null;
    }

    /// <summary>The cutout these arguments ask of <paramref name="source"/>: its file, cut its way.</summary>
    public CutoutSpec ToSpec(ICutoutSource source)
        => source.Bind(new CutoutSpec { Region = Region(), BandMin = BandMin, BandMax = BandMax, Extensions = Extensions ?? [] });

    /// <summary>
    /// The file meant, and the way of cutting it: the file named, or the only one that can be cut; the
    /// way asked for, or the best that can cut it (<see cref="CutoutSources.Preferred"/>). A choice left
    /// to guess between several files is refused with their names, and a way that cannot cut with its
    /// reason, rather than guessed.
    /// </summary>
    public ICutoutSource PickSource(IReadOnlyList<ICutoutSource> sources)
    {
        if (sources.Count == 0)
            throw new McpToolException(new InvalidArgument(
                "none of this observation's files can be cut out: CADC offers no cutout service for them, and none is on this computer; download_observation fetches the whole file, which can then be cut locally"));

        var named = (ArtifactId ?? string.Empty).Trim();
        var files = sources.Select(s => s.File.ArtifactId).Distinct().ToList();
        var candidates = named.Length == 0
            ? sources
            : sources.Where(s => s.File.ArtifactId == named || s.File.FileName == named).ToList();

        if (candidates.Count == 0)
            throw new McpToolException(new InvalidArgument(
                $"no file '{named}' can be cut from this observation; the ones that can: {string.Join(", ", files)}"));
        if (candidates.Select(s => s.File.ArtifactId).Distinct().Count() > 1)
            throw new McpToolException(new InvalidArgument(
                $"this observation has {files.Count} files that can be cut; name one as artifactId: {string.Join(", ", files)}"));

        var ways = CutoutSources.Preferred(candidates, CutBy);
        var chosen = CutBy is { } asked ? ways.FirstOrDefault(w => w.Method == asked) : ways[0];
        if (chosen is null)
            throw new McpToolException(new InvalidArgument(CutBy == CutoutMethod.Local
                ? "this file is not on this computer to cut locally; download_observation fetches it, or cut it with cutBy 'soda'"
                : "CADC offers no cutout service for this file; cut it with cutBy 'local' once it is downloaded"));
        if (chosen.Unavailable is { } why)
            throw new McpToolException(new InvalidArgument($"this file cannot be cut {Say(chosen.Method)}: {why}"));
        return chosen;
    }

    private static string Say(CutoutMethod method) => method == CutoutMethod.Local ? "locally" : "on CADC's side";
}

/// <summary>One file an agent could cut, one way, and what it can be cut by — or why this way cannot cut it.</summary>
public sealed record CutoutFileOption(
    string ArtifactId,
    string FileName,
    CutoutMethod CutBy,
    string? Unavailable,
    IReadOnlyList<string> Parameters,
    IReadOnlyList<string> Extensions,
    SkyRegion? Footprint,
    SkyRegion? BoundingCircle,
    double? BandMinMetres,
    double? BandMaxMetres,
    long? WholeFileBytes,
    CutoutSpec Suggested,
    string SuggestedSummary,
    long? SuggestedBytes);

/// <summary>What get_cutout_options answers.</summary>
public sealed record CutoutOptions(string PublisherId, IReadOnlyList<CutoutFileOption> Files, string? Note)
{
    /// <summary>
    /// Each file's options, each way it can be cut, with the cutout the editor would open on — from the
    /// last search when it looked at this file. Pure, so it is tested without a network or a screen.
    /// </summary>
    public static CutoutOptions From(string publisherId, IReadOnlyList<ICutoutSource> sources, CutoutHints? hints)
    {
        var options = sources.Select(source =>
        {
            var f = source.File;
            var suggested = source.Suggest(hints);
            return new CutoutFileOption(f.ArtifactId, f.FileName, source.Method, source.Unavailable, f.Parameters.Order().ToList(), f.Extensions,
                f.Footprint, f.BoundingCircle, f.BandMin, f.BandMax, source.WholeFileBytes,
                suggested, suggested.Summary, source.EstimateBytes(suggested));
        }).ToList();

        var note = options.Count == 0
            ? "none of this observation's files can be cut out: CADC offers no cutout service for them, and none is on this computer; download_observation fetches the whole file, which can then be cut locally"
            : null;
        return new CutoutOptions(publisherId, options, note);
    }
}

/// <summary><c>get_cutout_options</c> — what an observation's files can be cut by, and a suggested cutout of each.</summary>
public sealed class GetCutoutOptionsTool : JsonReadTool<GetCutoutOptionsTool.Args, CutoutOptions>
{
    private readonly Func<string, CancellationToken, Task<CutoutOptions>> _options;

    public GetCutoutOptionsTool(Func<string, CancellationToken, Task<CutoutOptions>> options) => _options = options;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_cutout_options",
        "What an observation's files can be CUT by, each way it can be cut: cutBy 'Soda' — on CADC's side, " +
        "only the part downloaded (a few MB of a 1.6 GB MegaPipe tile) — and 'Local' — from the observation's " +
        "file already on this computer: instant, offline, repeatable, and the only way for files CADC will not " +
        "cut, such as its HST mirror's. For each: the parameters it takes (CIRCLE, POLYGON, BAND …), the " +
        "file's footprint and wavelength range, its full size, why it cannot be cut this way when it cannot " +
        "(unavailable), the images of a multi-extension file a local cut can choose among (extensions), and the cutout the editor would suggest, from the last search's target and wavelengths " +
        "when they fall on the file, with its size (estimated for Soda, exact for Local). Read this before download_cutout.",
        """{"type":"object","properties":{"publisherId":{"type":"string"}},"required":["publisherId"],"additionalProperties":false}""");

    protected override async Task<CutoutOptions> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var id = (args.PublisherId ?? string.Empty).Trim();
        if (id.Length == 0) throw new McpToolException(new InvalidArgument("publisherId is required"));
        return await _options(id, ct);
    }

    public sealed record Args { public string? PublisherId { get; init; } }
}

/// <summary>What a download_cutout proposal carries: the observation and the cutout, already checked.</summary>
public sealed record DownloadCutoutPayload(string PublisherId, CutoutSpec Spec);

/// <summary>
/// <c>download_cutout</c> — propose downloading part of one file, cut on CADC's side. SemanticWrite.
///
/// <para>Checked before it is proposed, against the file, by the same <see cref="ICutoutSource.Check"/>
/// the editor shows: a region off the image is refused now, with the reason, rather than queued to fail
/// at apply time.</para>
/// </summary>
public sealed class DownloadCutoutTool : JsonWriteTool<CutoutArgs>
{
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<ICutoutSource>>> _sources;

    public DownloadCutoutTool(Func<string, CancellationToken, Task<IReadOnlyList<ICutoutSource>>> sources) => _sources = sources;

    public override McpVerbClass VerbClass => McpVerbClass.SemanticWrite;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "download_cutout",
        "Propose making a CUTOUT — part of one of an observation's files — into Research, where it is kept as a " +
        "cutout of the observation, never as the whole of it: cut on CADC's side by its SODA service " +
        "(cutBy 'soda'), or on this computer from the observation's file already downloaded (cutBy 'local'); " +
        "left out, locally when that file is here and can be cut, else by CADC. Give one region (circle, box " +
        "or polygon, degrees) and, for a cube, optionally bandMin/bandMax in metres. It is checked against the " +
        "file before it is queued: a region off the file is refused with the reason. Use get_cutout_options " +
        "first for the file's limits and a suggestion. Queues for the user under their auto-apply setting; " +
        "progress shows in the status bar.",
        "{\"type\":\"object\",\"properties\":{" + CutoutArgs.SchemaProperties + "},\"required\":[\"publisherId\"],\"additionalProperties\":false}");

    protected override async Task<ProposalPlan> PlanAsync(CutoutArgs args, McpToolContext context, CancellationToken ct)
    {
        var pid = (args.PublisherId ?? string.Empty).Trim();
        if (pid.Length == 0) throw new McpToolException(new InvalidArgument("publisherId is required"));

        var source = args.PickSource(await _sources(pid, ct));
        var spec = args.ToSpec(source);
        var check = source.Check(spec);
        if (!check.IsValid) throw new McpToolException(new InvalidArgument(string.Join(" ", check.Errors)));

        var verb = spec.CutBy == CutoutMethod.Local ? "Cut out locally" : "Download cutout";
        return ProposalPlan.Encoding("download_cutout",
            $"{verb} of {pid}: {spec.Summary}", new DownloadCutoutPayload(pid, spec));
    }
}

/// <summary>Applies <c>download_cutout</c> through the host's download — the app-owned downloader.</summary>
public sealed class DownloadCutoutApplier : IProposalApplier
{
    private readonly Func<DownloadCutoutPayload, Models.AgentAttribution?, Task> _download;

    public DownloadCutoutApplier(Func<DownloadCutoutPayload, Models.AgentAttribution?, Task> download) => _download = download;

    public string Kind => "download_cutout";

    public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
        => _download(ProposalPayload.Decode<DownloadCutoutPayload>(proposal), Agents.AgentAttributionStamp.ForProposal(proposal));
}

/// <summary>Where the cutout editor stands after an agent opened it.</summary>
public sealed record CutoutEditorShown(bool Shown, string? ArtifactId, string? Summary,
    IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings, string? Message = null)
{
    public static CutoutEditorShown Refused(string message) => new(false, null, null, [], [], message);
}

/// <summary>
/// <c>show_cutout_editor</c> — open the cutout editor on the person's screen, on one of an
/// observation's files, starting from a region the agent proposes (or the editor's own suggestion).
/// For showing: the person sees the region on the file's footprint, adjusts it, and downloads it
/// themselves — or the agent follows with download_cutout. Live-applied; changes nothing.
/// </summary>
public sealed class ShowCutoutEditorTool : JsonReadTool<CutoutArgs, CutoutEditorShown>
{
    private readonly Func<CutoutArgs, Task<CutoutEditorShown>> _show;

    public ShowCutoutEditorTool(Func<CutoutArgs, Task<CutoutEditorShown>> show) => _show = show;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "show_cutout_editor",
        "Open the cutout editor on the person's screen: the observation's detail page, its Files tab, the " +
        "editor for one file with its footprint drawn and a region on it — the one you give (circle, box or " +
        "polygon, degrees; bandMin/bandMax in metres), or the editor's own suggestion from the last search — " +
        "set to cut it the way you name (cutBy), or the best available. " +
        "The reply says what the editor shows and anything wrong with it. Nothing is downloaded: the person " +
        "adjusts and downloads it themselves, or you follow with download_cutout. Point at its controls with " +
        "point_at_ui (CutoutWayChoice, CutoutSky, CutoutDownloadButton …).",
        "{\"type\":\"object\",\"properties\":{" + CutoutArgs.SchemaProperties + "},\"required\":[\"publisherId\"],\"additionalProperties\":false}");

    protected override async Task<CutoutEditorShown> HandleAsync(CutoutArgs args, McpToolContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args.PublisherId))
            throw new McpToolException(new InvalidArgument("publisherId is required"));
        _ = args.Region(); // one region at most — refused before anything opens
        return await _show(args);
    }
}
