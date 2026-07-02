namespace CanfarDesktop.Mcp.Tools.Read;

/// <summary>The resolved arguments handed to the injected VizieR search (radius already in degrees).</summary>
public sealed record VizierConeSearchRequest(
    string Catalogue, double RaDeg, double DecDeg, double RadiusDeg, string RaColumn, string DecColumn, int MaxRec);

/// <summary>
/// <c>vizier_cone_search</c> — cone-search a VizieR catalogue at CDS. Wraps
/// <see cref="Services.VizierService"/> through an injected delegate so it stays pure and
/// unit-testable (mirrors the macOS closure shape). The 90s deadline accommodates the multi-host
/// VizieR fallback chain (each host gets ~20s before the failover rotates to the next).
/// </summary>
public sealed class VizierConeSearchTool : JsonReadTool<VizierConeSearchTool.Args, VizierConeSearchTool.Output>
{
    public const int DefaultMaxRec = 500;
    public const int MaxRecCap = 5000;

    private readonly Func<VizierConeSearchRequest, CancellationToken, Task<(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows)>> _search;

    public VizierConeSearchTool(
        Func<VizierConeSearchRequest, CancellationToken, Task<(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows)>> search)
        => _search = search;

    protected override TimeSpan Timeout => TimeSpan.FromSeconds(90);

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "vizier_cone_search",
        "Cone-search a VizieR catalogue at CDS. Standard pattern for catalogue cross-matches against " +
        "any of VizieR's many holdings (Clement+2001 variables-in-globular-clusters as V/97, OGLE " +
        "catalogues, ASAS-SN, ZTF, etc.). Public, no auth. `catalogue` is the VizieR identifier exactly " +
        "(`V/97/catalog`, `B/vsx/vsx`, `I/355/gaiadr3`, …). Position columns default to RAJ2000 / DEJ2000 " +
        "— override `raColumn` / `decColumn` if the specific catalogue uses different names. " +
        "`radiusArcsec` is in arcseconds for the convenience of typical cluster work; the tool converts " +
        "to degrees internally. Returns parsed rows + a `probablyTruncated` hint when the row count hit the cap.",
        """
        {"type":"object","required":["catalogue","raDeg","decDeg","radiusArcsec"],"properties":{
          "catalogue":{"type":"string","minLength":1,"description":"VizieR catalogue identifier, e.g. V/97/catalog."},
          "raDeg":{"type":"number"},
          "decDeg":{"type":"number"},
          "radiusArcsec":{"type":"number","minimum":0,"description":"Cone radius in arcseconds; converted to degrees internally."},
          "raColumn":{"type":"string","description":"Override the RA column name. Default: RAJ2000."},
          "decColumn":{"type":"string","description":"Override the Dec column name. Default: DEJ2000."},
          "maxRec":{"type":"integer","minimum":1,"maximum":5000,"description":"Row cap; default 500."}
        },"additionalProperties":false}
        """);

    protected override async Task<Output> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var catalogue = (args.Catalogue ?? string.Empty).Trim();
        if (catalogue.Length == 0)
            throw new McpToolException(new InvalidArgument("catalogue is required"));
        if (args.RaDeg is not double raDeg || args.DecDeg is not double decDeg)
            throw new McpToolException(new InvalidArgument("raDeg and decDeg are required"));
        if (args.RadiusArcsec is not double radiusArcsec)
            throw new McpToolException(new InvalidArgument("radiusArcsec is required"));
        if (radiusArcsec < 0)
            throw new McpToolException(new InvalidArgument("radiusArcsec must be >= 0"));
        if (args.MaxRec is < 1 or > MaxRecCap)
            throw new McpToolException(new InvalidArgument($"maxRec must be between 1 and {MaxRecCap}"));

        var maxRec = args.MaxRec ?? DefaultMaxRec;
        var raColumn = string.IsNullOrWhiteSpace(args.RaColumn) ? "RAJ2000" : args.RaColumn!.Trim();
        var decColumn = string.IsNullOrWhiteSpace(args.DecColumn) ? "DEJ2000" : args.DecColumn!.Trim();
        var radiusDeg = radiusArcsec / 3600.0;

        IReadOnlyList<string> headers;
        IReadOnlyList<IReadOnlyList<string>> rows;
        try
        {
            (headers, rows) = await _search(
                new VizierConeSearchRequest(catalogue, raDeg, decDeg, radiusDeg, raColumn, decColumn, maxRec), ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpToolException(new BackendError($"vizier_cone_search: {ex.Message}"));
        }

        return new Output(catalogue, headers, rows, rows.Count, rows.Count >= maxRec);
    }

    public sealed record Args
    {
        public string? Catalogue { get; init; }
        public double? RaDeg { get; init; }
        public double? DecDeg { get; init; }
        public double? RadiusArcsec { get; init; }
        public string? RaColumn { get; init; }
        public string? DecColumn { get; init; }
        public int? MaxRec { get; init; }
    }

    /// <summary><c>probablyTruncated</c> is true when the row count hit the requested cap, meaning the
    /// server probably had more matches — widen <c>maxRec</c> or narrow the cone.</summary>
    public sealed record Output(
        string Catalogue, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows,
        int RowCount, bool ProbablyTruncated);
}
