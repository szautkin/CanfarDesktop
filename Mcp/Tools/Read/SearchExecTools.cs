using System.Globalization;
using CanfarDesktop.Models;

namespace CanfarDesktop.Mcp.Tools.Read;

/// <summary>
/// <c>resolve_target</c> — resolve an astronomical target name (e.g. "M31") to ICRS RA/Dec via the
/// CADC name resolver. Wraps <c>ITAPService.ResolveTargetAsync(target, service)</c> through an
/// injected delegate, so it stays pure and unit-testable.
/// </summary>
public sealed class ResolveTargetTool : JsonReadTool<ResolveTargetTool.Args, ResolveTargetTool.Output>
{
    private readonly Func<string, string, Task<ResolverResult?>> _resolve;

    public ResolveTargetTool(Func<string, string, Task<ResolverResult?>> resolve) => _resolve = resolve;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "resolve_target",
        "Resolve an astronomical target name (e.g. M31, NGC 224) to ICRS RA/Dec degrees using the CADC name resolver.",
        """
        {"type":"object","properties":{
          "target":{"type":"string","description":"Target name to resolve (e.g. M31)"},
          "service":{"type":"string","description":"Resolver service: ALL (default), NED, SIMBAD, or VIZIER"}
        },"required":["target"],"additionalProperties":false}
        """);

    protected override async Task<Output> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args.Target))
            throw new McpToolException(new InvalidArgument("target is required"));

        var service = string.IsNullOrWhiteSpace(args.Service) ? "ALL" : args.Service.Trim();

        var result = await _resolve(args.Target.Trim(), service);
        if (result is null)
            throw new McpToolException(new TargetNotResolved(args.Target.Trim()));

        return new Output(result.Target, result.RA, result.Dec, result.CoordSys, result.ObjectType, result.Service);
    }

    public sealed record Args
    {
        public string Target { get; init; } = string.Empty;
        public string? Service { get; init; }
    }

    public sealed record Output(string Target, double Ra, double Dec, string? CoordSys, string? ObjectType, string? Service);
}

/// <summary>
/// <c>search_observations</c> — run an observation search against the CADC TAP service. Either pass an
/// explicit <c>adql</c> string, or a spatial cone (<c>target</c> name OR <c>ra</c>+<c>dec</c>, plus an
/// optional <c>radius</c> in degrees), in which case a CIRCLE('ICRS', …) INTERSECTS query against
/// caom2.Plane is built. Wraps <c>ITAPService.ExecuteQueryAsync(adql, maxRecords)</c> (the only query
/// entry point ITAPService exposes — there is no native spatial overload) and an injected resolver for
/// name → RA/Dec. Returns the column names plus a capped number of rows and the total row count.
/// </summary>
public sealed class SearchObservationsTool : JsonReadTool<SearchObservationsTool.Args, SearchObservationsTool.Output>
{
    /// <summary>Hard cap on rows requested from the backend and returned to the caller.</summary>
    public const int MaxRowsCap = 1000;

    /// <summary>Default cone radius (degrees) when a spatial target is given without a radius (~1 arcmin).</summary>
    private const double DefaultRadiusDeg = 0.0167;

    private readonly Func<string, int, Task<SearchResults>> _execute;
    private readonly Func<string, string, Task<ResolverResult?>> _resolve;

    public SearchObservationsTool(
        Func<string, int, Task<SearchResults>> execute,
        Func<string, string, Task<ResolverResult?>> resolve)
    {
        _execute = execute;
        _resolve = resolve;
    }

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "search_observations",
        "Search CADC observations. Provide either an explicit ADQL query, or a spatial cone " +
        "(target name OR ra+dec in degrees, with an optional radius in degrees). Returns column names, " +
        "a capped set of rows, and the total row count.",
        """
        {"type":"object","properties":{
          "adql":{"type":"string","description":"Explicit ADQL query. If set, the spatial fields are ignored."},
          "target":{"type":"string","description":"Target name resolved to RA/Dec for a cone search."},
          "ra":{"type":"number","description":"ICRS Right Ascension in degrees (with dec)."},
          "dec":{"type":"number","description":"ICRS Declination in degrees (with ra)."},
          "radius":{"type":"number","description":"Cone radius in degrees (default ~0.0167, i.e. 1 arcmin)."},
          "maxRows":{"type":"integer","minimum":1,"maximum":1000,"description":"Max rows to request/return (capped at 1000)."}
        },"additionalProperties":false}
        """);

    protected override async Task<Output> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var maxRows = args.MaxRows is > 0 ? Math.Min(args.MaxRows.Value, MaxRowsCap) : MaxRowsCap;

        var adql = await BuildAdqlAsync(args, maxRows);

        var results = await _execute(adql, maxRows);

        var rows = results.Rows
            .Take(maxRows)
            .Select(r => results.Columns.Select(col => r.Get(col)).ToArray() as IReadOnlyList<string>)
            .ToList();

        return new Output(adql, results.Columns, rows.Count, results.TotalRows, rows);
    }

    private async Task<string> BuildAdqlAsync(Args args, int maxRows)
    {
        if (!string.IsNullOrWhiteSpace(args.Adql))
            return args.Adql.Trim();

        double ra, dec;
        if (args.Ra is { } r && args.Dec is { } d)
        {
            ra = r;
            dec = d;
        }
        else if (!string.IsNullOrWhiteSpace(args.Target))
        {
            var resolved = await _resolve(args.Target.Trim(), "ALL");
            if (resolved is null)
                throw new McpToolException(new TargetNotResolved(args.Target.Trim()));
            ra = resolved.RA;
            dec = resolved.Dec;
        }
        else
        {
            throw new McpToolException(new InvalidArgument(
                "Provide 'adql', or a spatial cone via 'target' or 'ra'+'dec'."));
        }

        var radius = args.Radius is > 0 ? args.Radius.Value : DefaultRadiusDeg;
        return BuildConeAdql(ra, dec, radius, maxRows);
    }

    /// <summary>
    /// Build a minimal CIRCLE('ICRS', …) INTERSECTS cone query against caom2.Plane/Observation. Mirrors
    /// the spatial clause shape used by <see cref="CanfarDesktop.Helpers.ADQLBuilder"/>.
    /// </summary>
    private static string BuildConeAdql(double ra, double dec, double radius, int maxRows) =>
        $"""
        SELECT TOP {maxRows}
        Observation.observationID,
        Observation.collection,
        Observation.instrument_name AS "Instrument",
        Observation.target_name AS "Target Name",
        COORD1(CENTROID(Plane.position_bounds)) AS "RA (J2000.0)",
        COORD2(CENTROID(Plane.position_bounds)) AS "Dec. (J2000.0)",
        Plane.calibrationLevel AS "Cal. Lev.",
        Plane.publisherID
        FROM caom2.Plane AS Plane JOIN caom2.Observation AS Observation ON Plane.obsID = Observation.obsID
        WHERE ( Plane.quality_flag IS NULL OR Plane.quality_flag != 'junk' )
        AND INTERSECTS( CIRCLE('ICRS', {F(ra)}, {F(dec)}, {F(radius)}), Plane.position_bounds ) = 1
        """;

    private static string F(double v) => v.ToString("G10", CultureInfo.InvariantCulture);

    public sealed record Args
    {
        public string? Adql { get; init; }
        public string? Target { get; init; }
        public double? Ra { get; init; }
        public double? Dec { get; init; }
        public double? Radius { get; init; }
        public int? MaxRows { get; init; }
    }

    /// <summary>
    /// Compact result: the ADQL actually run, the column names, the count of rows returned (after the
    /// cap), the total rows the backend produced, and the row cells aligned to <see cref="Columns"/>.
    /// </summary>
    public sealed record Output(
        string Adql,
        IReadOnlyList<string> Columns,
        int ReturnedRows,
        int TotalRows,
        IReadOnlyList<IReadOnlyList<string>> Rows);
}