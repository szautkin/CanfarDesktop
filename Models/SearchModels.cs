namespace CanfarDesktop.Models;

/// <summary>
/// State of the search form, used for building ADQL and persisting recent searches.
/// </summary>
public class SearchFormState
{
    // Observation constraints
    public string ObservationId { get; set; } = string.Empty;
    public string ProposalPi { get; set; } = string.Empty;
    public string ProposalId { get; set; } = string.Empty;
    public string ProposalTitle { get; set; } = string.Empty;
    public string ProposalKeywords { get; set; } = string.Empty;
    public string Intent { get; set; } = string.Empty; // "", "science", "calibration"
    public bool PublicOnly { get; set; }

    // Spatial constraints
    public string Target { get; set; } = string.Empty;
    public string ResolverService { get; set; } = "ALL";
    public double? ResolvedRA { get; set; }
    public double? ResolvedDec { get; set; }
    public double SearchRadius { get; set; } = 0.0167;
    public string PixelScale { get; set; } = string.Empty;
    public string PixelScaleUnit { get; set; } = "arcsec";
    public bool SpatialCutout { get; set; }

    // Temporal constraints
    public string ObservationDate { get; set; } = string.Empty;     // range syntax: "2020..2021", "> 2019"
    public string DatePreset { get; set; } = string.Empty;          // "", "Last24h", "LastWeek", "LastMonth"
    public string DateStart { get; set; } = string.Empty;           // legacy simple date
    public string DateEnd { get; set; } = string.Empty;             // legacy simple date
    public string IntegrationTimeMin { get; set; } = string.Empty;
    public string IntegrationTimeMax { get; set; } = string.Empty;
    public string IntegrationTimeUnit { get; set; } = "s";
    public string TimeSpan { get; set; } = string.Empty;            // range syntax for time bounds width
    public string TimeSpanUnit { get; set; } = "d";
    public string DataRelease { get; set; } = string.Empty;

    // Spectral constraints
    public string WavelengthMin { get; set; } = string.Empty;       // legacy
    public string WavelengthMax { get; set; } = string.Empty;       // legacy
    public string SpectralCoverage { get; set; } = string.Empty;    // range syntax with overlap
    public string SpectralCoverageUnit { get; set; } = "nm";
    public string SpectralSampling { get; set; } = string.Empty;
    public string SpectralSamplingUnit { get; set; } = "nm";
    public string ResolvingPower { get; set; } = string.Empty;      // dimensionless range
    public string BandpassWidth { get; set; } = string.Empty;
    public string BandpassWidthUnit { get; set; } = "nm";
    public string RestFrameEnergy { get; set; } = string.Empty;
    public string RestFrameEnergyUnit { get; set; } = "nm";
    public bool SpectralCutout { get; set; }

    // Data train selections (comma-separated)
    public string Bands { get; set; } = string.Empty;
    public string Collections { get; set; } = string.Empty;
    public string Instruments { get; set; } = string.Empty;
    public string Filters { get; set; } = string.Empty;
    public string CalibrationLevels { get; set; } = string.Empty;
    public string DataProductTypes { get; set; } = string.Empty;
    public string ObservationTypes { get; set; } = string.Empty;

    // Max records
    public int MaxRecords { get; set; } = 10000;
}

/// <summary>
/// Result from the CADC target name resolver.
/// </summary>
public class ResolverResult
{
    public string Target { get; set; } = string.Empty;
    public double RA { get; set; }
    public double Dec { get; set; }
    public string? CoordSys { get; set; }
    public string? ObjectType { get; set; }
    public string? Service { get; set; }
}

/// <summary>
/// A single row from TAP query results (generic key-value).
/// </summary>
public class SearchResultRow
{
    public Dictionary<string, string> Values { get; set; } = new();
    public string Get(string key) => Values.TryGetValue(key, out var v) ? v : string.Empty;
}

/// <summary>
/// Complete TAP query result set.
/// </summary>
public class SearchResults
{
    public List<string> Columns { get; set; } = [];
    public List<SearchResultRow> Rows { get; set; } = [];
    public int TotalRows => Rows.Count;
    public string? Query { get; set; }
}

/// <summary>
/// A single row from the data train enumfield query.
/// </summary>
public class DataTrainRow
{
    public string Band { get; set; } = string.Empty;
    public string Collection { get; set; } = string.Empty;
    public string Instrument { get; set; } = string.Empty;
    public string Filter { get; set; } = string.Empty;
    public string CalibrationLevel { get; set; } = string.Empty;
    public string DataProductType { get; set; } = string.Empty;
    public string ObservationType { get; set; } = string.Empty;
    public bool IsFresh { get; set; }
}

/// <summary>
/// Column metadata for search results display.
/// </summary>
public class ResultColumnInfo
{
    public string Key { get; set; } = string.Empty;       // cleaned key for formatting/visibility
    public string Label { get; set; } = string.Empty;      // display label
    public string Header { get; set; } = string.Empty;     // original CSV header (used as row.Values key)
    public bool Visible { get; set; }
    public int Width { get; set; } = 80;
}

/// <summary>
/// A saved ADQL query.
/// </summary>
public class SavedQuery
{
    public string Name { get; set; } = string.Empty;
    public string Adql { get; set; } = string.Empty;
    public DateTime SavedAt { get; set; }
}

/// <summary>
/// A recent search entry with form state snapshot.
/// </summary>
public class RecentSearch
{
    public string Summary { get; set; } = string.Empty;
    public string Adql { get; set; } = string.Empty;
    public SearchFormState FormState { get; set; } = new();
    public int ResultCount { get; set; }
    public DateTime SearchedAt { get; set; }
}
