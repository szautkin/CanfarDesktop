namespace CanfarDesktop.Models.Cutouts;

/// <summary>
/// One file's cutout service, as its DataLink answer describes it: where SODA is, what the file is
/// called there, which parameters it takes, and the limits of each.
///
/// <para>Per file, because files differ: a MegaPipe image takes CIRCLE and POLYGON, a JCMT cube takes
/// BAND as well. An editor built from this shows only the fields that file can use, and a request
/// checked against it cannot ask for what the file does not have.</para>
/// </summary>
public sealed record SodaDescriptor
{
    /// <summary>The SODA sync endpoint — https only, as every DataLink URL the app follows.</summary>
    public required string AccessUrl { get; init; }

    /// <summary>The file, as SODA's ID parameter names it.</summary>
    public required string ArtifactId { get; init; }

    /// <summary>Upper-case names of the parameters the service lists for this file (ID, POS, CIRCLE, …).</summary>
    public IReadOnlySet<string> Parameters { get; init; } = new HashSet<string>();

    /// <summary>The file's footprint: POLYGON's MAX when given, else CIRCLE's.</summary>
    public SkyRegion? Footprint { get; init; }

    /// <summary>CIRCLE's MAX: the smallest circle holding the whole file.</summary>
    public SkyRegion? BoundingCircle { get; init; }

    /// <summary>BAND's limits, metres.</summary>
    public double? BandMin { get; init; }
    public double? BandMax { get; init; }

    /// <summary>TIME's limits, MJD.</summary>
    public double? TimeMin { get; init; }
    public double? TimeMax { get; init; }

    /// <summary>POL's allowed states, when it lists them.</summary>
    public IReadOnlyList<string> PolStates { get; init; } = [];

    public bool Supports(string parameter) => Parameters.Contains(parameter.ToUpperInvariant());

    /// <summary>Whether it can be cut on the sky at all.</summary>
    public bool SupportsSky => Supports("CIRCLE") || Supports("POLYGON");

    /// <summary>The file's own name — the last part of its ID, as the observation view names artifacts.</summary>
    public string FileName => Helpers.Caom2Format.ArtifactFileName(ArtifactId);
}
