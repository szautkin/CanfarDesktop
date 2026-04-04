using System.Globalization;

namespace CanfarDesktop.Helpers;

/// <summary>
/// Converts active per-column client-side filters into ADQL WHERE clauses.
/// Pure static, no UI dependencies, fully testable.
/// </summary>
public static class FilterToAdqlConverter
{
    /// <summary>
    /// Maps cleaned column keys to their qualified ADQL column names.
    /// Includes computed columns that use ADQL functions.
    /// </summary>
    private static readonly Dictionary<string, string> ColumnToAdql = new(StringComparer.OrdinalIgnoreCase)
    {
        ["observationid"] = "Observation.observationID",
        ["collection"] = "Observation.collection",
        ["targetname"] = "Observation.target_name",
        ["instrument"] = "Observation.instrument_name",
        ["filter"] = "Plane.energy_bandpassName",
        ["callev"] = "Plane.calibrationLevel",
        ["obstype"] = "Observation.type",
        ["proposalid"] = "Observation.proposal_id",
        ["piname"] = "Observation.proposal_pi",
        ["obsid"] = "Observation.observationID",
        ["datatype"] = "Plane.dataProductType",
        ["band"] = "Plane.energy_emBand",
        ["intent"] = "Observation.intent",
        ["ra(j20000)"] = "COORD1(CENTROID(Plane.position_bounds))",
        ["dec(j20000)"] = "COORD2(CENTROID(Plane.position_bounds))",
        ["startdate"] = "Plane.time_bounds_lower",
        ["enddate"] = "Plane.time_bounds_upper",
        ["inttime"] = "Plane.time_exposure",
        ["minwavelength"] = "Plane.energy_bounds_lower",
        ["maxwavelength"] = "Plane.energy_bounds_upper",
        ["pixelscale"] = "Plane.position_sampleSize",
        ["resolvingpower"] = "Plane.energy_resolvingPower",
        ["fieldofview"] = "AREA(Plane.position_bounds)",
    };

    /// <summary>
    /// Convert active column filters into an ADQL WHERE fragment.
    /// Returns null if no valid filters.
    /// </summary>
    public static string? ConvertFilters(IReadOnlyDictionary<string, string> filters)
    {
        if (filters.Count == 0) return null;

        var clauses = new List<string>();

        foreach (var (key, text) in filters)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;

            var cleanKey = CellFormatter.CleanKey(key);
            if (!ColumnToAdql.TryGetValue(cleanKey, out var adqlCol)) continue;

            var clause = BuildClause(adqlCol, text.Trim());
            if (clause is not null) clauses.Add(clause);
        }

        return clauses.Count > 0 ? string.Join("\nAND ", clauses) : null;
    }

    /// <summary>
    /// Append filter WHERE fragment to an existing ADQL query.
    /// Returns original query if no filters are active.
    /// </summary>
    public static string AppendToQuery(string baseAdql, IReadOnlyDictionary<string, string> filters)
    {
        var fragment = ConvertFilters(filters);
        if (fragment is null) return baseAdql;
        return $"{baseAdql.TrimEnd()}\nAND {fragment}";
    }

    private static string? BuildClause(string adqlCol, string value)
    {
        // Try numeric: exact match
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
            return $"{adqlCol} = {num.ToString("G10", CultureInfo.InvariantCulture)}";

        // String: case-insensitive LIKE
        var escaped = value.Replace("'", "''").Replace("%", "\\%").Replace("_", "\\_");
        return $"lower({adqlCol}) LIKE '%{escaped.ToLower()}%'";
    }
}
