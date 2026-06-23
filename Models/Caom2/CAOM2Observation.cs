namespace CanfarDesktop.Models.Caom2;

/// <summary>
/// Domain model for the CAOM-2 observation document returned by
/// caom2ops/meta?ID=caom:{collection}/{observationID}.
/// Modelled at the level of detail the result detail viewer needs; the parser
/// ignores unknown elements so additive schema changes won't break it.
/// </summary>
public record CAOM2Observation
{
    public string Collection { get; init; } = string.Empty;
    public string ObservationID { get; init; } = string.Empty;
    public string? ObservationType { get; init; }   // e.g. "OBJECT" / "DARK"
    public string? Intent { get; init; }             // "science" | "calibration"
    public string? SequenceNumber { get; init; }
    public DateTimeOffset? MetaRelease { get; init; }
    public string? Algorithm { get; init; }          // "exposure" / "coadd" / ...

    public Caom2Proposal? Proposal { get; init; }
    public Caom2Target? Target { get; init; }
    public Caom2Telescope? Telescope { get; init; }
    public Caom2Instrument? Instrument { get; init; }
    public Caom2Environment? Environment { get; init; }

    public IReadOnlyList<Caom2Plane> Planes { get; init; } = [];
}

public record Caom2Proposal
{
    public string? Id { get; init; }
    public string? Pi { get; init; }
    public string? Project { get; init; }
    public string? Title { get; init; }
    public IReadOnlyList<string> Keywords { get; init; } = [];
}

public record Caom2Target
{
    public string? Name { get; init; }
    public string? Type { get; init; }
    public bool? Standard { get; init; }
    public double? Redshift { get; init; }
    public bool? Moving { get; init; }
    public IReadOnlyList<string> Keywords { get; init; } = [];
}

/// <summary>Geocentric ITRF position in metres.</summary>
public record Caom2GeoLocation(double X, double Y, double Z);

public record Caom2Telescope
{
    public string? Name { get; init; }
    public Caom2GeoLocation? GeoLocation { get; init; }
    public IReadOnlyList<string> Keywords { get; init; } = [];
}

public record Caom2Instrument
{
    public string? Name { get; init; }
    public IReadOnlyList<string> Keywords { get; init; } = [];
}

public record Caom2Environment
{
    public double? Seeing { get; init; }
    public double? Humidity { get; init; }
    public double? Elevation { get; init; }
    public double? Tau { get; init; }
    public double? WavelengthTau { get; init; }
    public double? AmbientTemp { get; init; }
    public bool? Photometric { get; init; }
}

public record Caom2Plane
{
    public string ProductID { get; init; } = string.Empty;
    public string? CreatorID { get; init; }
    public DateTimeOffset? MetaRelease { get; init; }
    public DateTimeOffset? DataRelease { get; init; }
    public string? DataProductType { get; init; }   // image / spectrum / cube / ...
    public int? CalibrationLevel { get; init; }

    public Caom2Provenance? Provenance { get; init; }
    public Caom2Metrics? Metrics { get; init; }
    public string? Quality { get; init; }            // junk / good / ...

    public Caom2Position? Position { get; init; }
    public Caom2Energy? Energy { get; init; }
    public Caom2Time? Time { get; init; }
    public Caom2Polarization? Polarization { get; init; }

    public IReadOnlyList<Caom2Artifact> Artifacts { get; init; } = [];
}

public record Caom2Provenance
{
    public string? Name { get; init; }
    public string? Version { get; init; }
    public string? Project { get; init; }
    public string? Producer { get; init; }
    public string? RunID { get; init; }
    public string? Reference { get; init; }
    public DateTimeOffset? LastExecuted { get; init; }
    public IReadOnlyList<string> Keywords { get; init; } = [];
    /// <summary>Plane URIs of upstream observations.</summary>
    public IReadOnlyList<string> Inputs { get; init; } = [];
}

public record Caom2Metrics
{
    public double? SourceNumberDensity { get; init; }
    public double? Background { get; init; }
    public double? BackgroundStddev { get; init; }
    public double? FluxDensityLimit { get; init; }
    public double? MagLimit { get; init; }
    public double? SampleSNR { get; init; }
}

/// <summary>A footprint vertex in degrees.</summary>
public record Caom2SkyVertex(double Ra, double Dec);

public record Caom2PixelDimension(int NAxis1, int NAxis2);

/// <summary>Spatial coverage. Polygon carries the footprint outline as (RA, Dec) in degrees.</summary>
public record Caom2Position
{
    public IReadOnlyList<Caom2SkyVertex> Polygon { get; init; } = [];
    public Caom2PixelDimension? DimensionPixels { get; init; }
    public double? ResolutionArcsec { get; init; }
    public double? SampleSizeArcsec { get; init; }
    public bool? TimeDependent { get; init; }
}

/// <summary>Spectral coverage. Bounds in metres (TAP/CAOM2 native).</summary>
public record Caom2Energy
{
    public double? LowerMetres { get; init; }
    public double? UpperMetres { get; init; }
    public double? ResolvingPower { get; init; }
    public string? BandpassName { get; init; }
    public string? EmBand { get; init; }
    public double? RestWavMetres { get; init; }
}

/// <summary>Temporal coverage. Bounds in MJD; exposure in seconds.</summary>
public record Caom2Time
{
    public double? LowerMJD { get; init; }
    public double? UpperMJD { get; init; }
    public double? ExposureSeconds { get; init; }
}

public record Caom2Polarization
{
    /// <summary>Stokes states present (free-form: "I", "Q", "U", "V", "RR", ...).</summary>
    public IReadOnlyList<string> States { get; init; } = [];
}

public record Caom2Artifact
{
    public string Uri { get; init; } = string.Empty;
    public string? ProductType { get; init; }       // science / weight / preview / aux
    public string? ReleaseType { get; init; }        // data / meta
    public long? ContentLength { get; init; }
    public string? ContentType { get; init; }
    public string? ContentChecksum { get; init; }
}
