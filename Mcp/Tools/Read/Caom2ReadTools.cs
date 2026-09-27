using CanfarDesktop.Models;
using CanfarDesktop.Models.Caom2;
using CanfarDesktop.Services;

namespace CanfarDesktop.Mcp.Tools.Read;

/// <summary>Compact CAOM2 observation metadata summary for MCP output.</summary>
public sealed record Caom2ObservationSummary(
    string Collection, string ObservationId, string? ObservationType, string? Intent,
    string? Algorithm, DateTimeOffset? MetaRelease,
    string? ProposalId, string? ProposalPi, string? ProposalProject, string? ProposalTitle,
    string? TargetName, string? TargetType, double? TargetRedshift,
    string? TelescopeName, string? InstrumentName,
    int PlaneCount, IReadOnlyList<Caom2PlaneSummary> Planes)
{
    public static Caom2ObservationSummary From(CAOM2Observation o) => new(
        o.Collection, o.ObservationID, o.ObservationType, o.Intent, o.Algorithm, o.MetaRelease,
        o.Proposal?.Id, o.Proposal?.Pi, o.Proposal?.Project, o.Proposal?.Title,
        o.Target?.Name, o.Target?.Type, o.Target?.Redshift,
        o.Telescope?.Name, o.Instrument?.Name,
        o.Planes.Count, o.Planes.Select(Caom2PlaneSummary.From).ToList());
}

/// <summary>One CAOM2 plane (data product) summarised for MCP output.</summary>
public sealed record Caom2PlaneSummary(
    string ProductId, string? DataProductType, int? CalibrationLevel, string? Quality)
{
    public static Caom2PlaneSummary From(Caom2Plane p) =>
        new(p.ProductID, p.DataProductType, p.CalibrationLevel, p.Quality);
}

/// <summary><c>get_observation_caom2</c> — the CAOM2 metadata document for one publisher id.</summary>
public sealed class GetObservationCaom2Tool : JsonReadTool<GetObservationCaom2Tool.Args, Caom2ObservationSummary>
{
    private readonly Func<string, CancellationToken, Task<Caom2Result>> _get;

    public GetObservationCaom2Tool(Func<string, CancellationToken, Task<Caom2Result>> get) => _get = get;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_observation_caom2",
        "Get the CAOM2 metadata (collection, target, proposal, telescope, instrument, planes) for one observation by its publisher id.",
        """{"type":"object","properties":{"publisherId":{"type":"string","description":"Observation publisher id (e.g. ivo://cadc.nrc.ca/CFHT?...)"}},"required":["publisherId"],"additionalProperties":false}""");

    protected override async Task<Caom2ObservationSummary> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args.PublisherId))
            throw new McpToolException(new InvalidArgument("publisherId is required"));

        var result = await _get(args.PublisherId, ct);
        switch (result.Status)
        {
            case Caom2Status.Success when result.Observation is not null:
                return Caom2ObservationSummary.From(result.Observation);
            case Caom2Status.AuthRequired:
                throw new McpToolException(new AuthRequired(result.Message ?? "This observation requires CADC sign-in."));
            case Caom2Status.NotFound:
                throw new McpToolException(new UnknownTarget(args.PublisherId));
            case Caom2Status.InvalidId:
                throw new McpToolException(new InvalidArgument(result.Message ?? $"Invalid publisher id: {args.PublisherId}"));
            default:
                throw new McpToolException(new BackendError(result.Message ?? $"CAOM2 metadata fetch failed ({result.Status})."));
        }
    }

    public sealed record Args
    {
        public string PublisherId { get; init; } = string.Empty;
    }
}

/// <summary>One downloadable DataLink artifact summarised for MCP output.</summary>
public sealed record DataLinkFileView(string Url, string ContentType, string Description, string Filename)
{
    public static DataLinkFileView From(DataLinkFile f) => new(f.Url, f.ContentType, f.Description, f.Filename);
}

/// <summary><c>get_data_links</c> — download / preview / thumbnail links for one publisher id.</summary>
public sealed class GetDataLinksTool : JsonReadTool<GetDataLinksTool.Args, GetDataLinksTool.Output>
{
    /// <summary>How much of a raw answer is given back: DataLink answers are tens of kilobytes; more is not one.</summary>
    public const int MaxRaw = 200_000;

    private readonly Func<string, CancellationToken, Task<DataLinkResult>> _get;
    private readonly Func<string, CancellationToken, Task<DataLinkAnswer>>? _fetch;

    /// <param name="fetch">DataLink's answer as it came, for <c>raw</c>; without it, raw is refused.</param>
    public GetDataLinksTool(Func<string, CancellationToken, Task<DataLinkResult>> get,
                            Func<string, CancellationToken, Task<DataLinkAnswer>>? fetch = null)
    {
        _get = get;
        _fetch = fetch;
    }

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_data_links",
        "Get the DataLink artifacts (download url, direct files, preview images, thumbnails) for one " +
        "observation by its publisher id. Each entry's 0-based position in `directFiles` is its " +
        "`artifactIndex` for download_observation — use it to fetch a SPECIFIC product (e.g. the science " +
        "cube, a moment map _mom0/1/2, or the integrated spectrum _spec) instead of the default first one. " +
        "`cutoutServices` counts the files CADC can cut; `problems` says why the answer holds less than it " +
        "might (DataLink refused, or described a cutout service the app could not read). `raw` also gives " +
        "DataLink's answer exactly as it came — for finding out why.",
        """{"type":"object","properties":{"publisherId":{"type":"string","description":"Observation publisher id"},"raw":{"type":"boolean","description":"Also return DataLink's answer as it came (default false)."}},"required":["publisherId"],"additionalProperties":false}""");

    protected override async Task<Output> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args.PublisherId))
            throw new McpToolException(new InvalidArgument("publisherId is required"));

        var result = await _get(args.PublisherId, ct);
        var files = result.DirectFiles.Select(DataLinkFileView.From).ToList();
        var output = new Output(
            result.DownloadUrl,
            files.Count, files,
            result.Previews.Count, result.Previews,
            result.Thumbnails.Count, result.Thumbnails,
            result.Cutouts.Count, result.Problems);

        if (args.Raw != true) return output;
        if (_fetch is null) throw new McpToolException(new NotImplemented("raw is not available here"));

        // Asked afresh rather than from the cache: the point is to see what DataLink says now.
        var answer = await _fetch(args.PublisherId, ct);
        var body = answer.Body;
        return output with
        {
            RawStatus = answer.Status,
            Raw = body is null || body.Length <= MaxRaw ? body : body[..MaxRaw],
            RawTruncated = body is { Length: > MaxRaw } ? true : null,
            RawProblem = answer.Problem,
        };
    }

    public sealed record Args
    {
        public string PublisherId { get; init; } = string.Empty;
        public bool? Raw { get; init; }
    }

    public sealed record Output(
        string? DownloadUrl,
        int DirectFileCount, IReadOnlyList<DataLinkFileView> DirectFiles,
        int PreviewCount, IReadOnlyList<string> Previews,
        int ThumbnailCount, IReadOnlyList<string> Thumbnails,
        int CutoutServices, IReadOnlyList<string> Problems)
    {
        public int? RawStatus { get; init; }
        public string? Raw { get; init; }
        public bool? RawTruncated { get; init; }
        public string? RawProblem { get; init; }
    }
}
