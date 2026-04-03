using System.Globalization;
using CanfarDesktop.Models;

namespace CanfarDesktop.Helpers;

public static class ADQLBuilder
{
    private const string SelectColumns = """
        Observation.observationID,
        Observation.collection,
        Observation.sequenceNumber,
        Plane.productID,
        COORD1(CENTROID(Plane.position_bounds)) AS "RA (J2000.0)",
        COORD2(CENTROID(Plane.position_bounds)) AS "Dec. (J2000.0)",
        Observation.target_name AS "Target Name",
        Plane.time_bounds_lower AS "Start Date",
        Plane.time_exposure AS "Int. Time",
        Observation.instrument_name AS "Instrument",
        Plane.energy_bandpassName AS "Filter",
        Plane.calibrationLevel AS "Cal. Lev.",
        Observation.type AS "Obs. Type",
        Observation.proposal_id AS "Proposal ID",
        Observation.proposal_pi AS "PI Name",
        Plane.dataRelease AS "Data Release",
        Observation.observationID AS "Obs. ID",
        Plane.energy_bounds_lower AS "Min. Wavelength",
        Plane.energy_bounds_upper AS "Max. Wavelength",
        AREA(Plane.position_bounds) AS "Field of View",
        Plane.position_sampleSize AS "Pixel Scale",
        Plane.energy_resolvingPower AS "Resolving Power",
        Plane.time_bounds_upper AS "End Date",
        Plane.dataProductType AS "Data Type",
        Observation.target_moving AS "Moving Target",
        Plane.provenance_name AS "Provenance Name",
        Observation.intent AS "Intent",
        Observation.target_type AS "Target Type",
        Observation.algorithm_name AS "Algorithm",
        Observation.proposal_title AS "Proposal Title",
        Observation.proposal_keywords AS "Proposal Keywords",
        Plane.position_resolution AS "Spatial Resolution",
        Plane.energy_transition_species AS "Molecule",
        Plane.energy_transition_transition AS "Transition",
        Plane.energy_emBand AS "Band",
        Plane.energy_bounds_width AS "Bandpass Width",
        Plane.energy_sampleSize AS "Energy Sample Size",
        Plane.energy_restwav AS "Rest Frame Energy",
        Plane.time_bounds_width AS "Time Span",
        Observation.requirements_flag AS "Quality",
        Plane.publisherID
        """;

    private const string FromClause =
        "caom2.Plane AS Plane JOIN caom2.Observation AS Observation ON Plane.obsID = Observation.obsID";

    private const string QualityFilter =
        "( Plane.quality_flag IS NULL OR Plane.quality_flag != 'junk' )";

    public static string Build(SearchFormState state)
    {
        var clauses = new List<string> { QualityFilter };

        AddObservationClauses(state, clauses);
        AddSpatialClauses(state, clauses);
        AddTemporalClauses(state, clauses);
        AddSpectralClauses(state, clauses);
        AddDataTrainClauses(state, clauses);
        AddMiscClauses(state, clauses);

        var where = string.Join("\nAND ", clauses);

        return $"""
            SELECT TOP {state.MaxRecords}
            {SelectColumns}
            FROM {FromClause}
            WHERE {where}
            """;
    }

    #region Observation

    private static void AddObservationClauses(SearchFormState s, List<string> c)
    {
        if (!string.IsNullOrWhiteSpace(s.ObservationId))
        {
            if (s.ObservationId.Contains('*'))
                c.Add($"lower(Observation.observationID) LIKE '{EscapeLike(s.ObservationId.ToLower()).Replace("*", "%")}'");
            else
                c.Add($"lower(Observation.observationID) = '{Escape(s.ObservationId.ToLower())}'");
        }

        AddLikeClause("Observation.proposal_pi", s.ProposalPi, c);
        AddLikeClause("Observation.proposal_id", s.ProposalId, c);
        AddLikeClause("Observation.proposal_title", s.ProposalTitle, c);
        AddLikeClause("Observation.proposal_keywords", s.ProposalKeywords, c);
    }

    private static void AddLikeClause(string column, string value, List<string> c)
    {
        if (!string.IsNullOrWhiteSpace(value))
            c.Add($"lower({column}) LIKE '%{EscapeLike(value.ToLower())}%'");
    }

    #endregion

    #region Spatial

    private static void AddSpatialClauses(SearchFormState s, List<string> c)
    {
        if (!string.IsNullOrWhiteSpace(s.Target) || s.ResolvedRA is not null)
        {
            if (s.ResolvedRA is not null && s.ResolvedDec is not null)
                c.Add($"INTERSECTS( CIRCLE('ICRS', {F(s.ResolvedRA.Value)}, {F(s.ResolvedDec.Value)}, {F(s.SearchRadius)}), Plane.position_bounds ) = 1");
            else if (!string.IsNullOrWhiteSpace(s.Target))
                c.Add($"lower(Observation.target_name) LIKE '%{EscapeLike(s.Target.ToLower())}%'");
        }

        // Pixel scale
        if (RangeParser.TryParse(s.PixelScale, out var psRange))
            AddConvertedRangeClause("Plane.position_sampleSize", psRange, s.PixelScaleUnit, c,
                (v, u) => UnitConverter.TryConvertToDegrees(v, u, out var d) ? d : (double?)null);
    }

    #endregion

    #region Temporal

    private static void AddTemporalClauses(SearchFormState s, List<string> c)
    {
        // Date preset takes priority
        if (!string.IsNullOrWhiteSpace(s.DatePreset))
        {
            var now = DateTime.UtcNow;
            var start = s.DatePreset switch
            {
                "Last24h" => now.AddDays(-1),
                "LastWeek" => now.AddDays(-7),
                "LastMonth" => now.AddMonths(-1),
                _ => (DateTime?)null
            };
            if (start is not null && TryParseDateToMJD(start.Value, out var mjdStart) && TryParseDateToMJD(now, out var mjdEnd))
                c.Add($"INTERSECTS( INTERVAL( {F(mjdStart)}, {F(mjdEnd)} ), Plane.time_bounds_samples ) = 1");
        }
        // Observation date with range syntax
        else if (RangeParser.TryParse(s.ObservationDate, out var dateRange))
        {
            AddDateRangeClause(dateRange, c);
        }
        // Legacy start/end date fallback
        else
        {
            var hasStart = TryParseDateToMJD(s.DateStart, out var mjdStart);
            var hasEnd = TryParseDateToMJD(s.DateEnd, out var mjdEnd);
            if (hasStart && hasEnd)
                c.Add($"INTERSECTS( INTERVAL( {F(mjdStart)}, {F(mjdEnd)} ), Plane.time_bounds_samples ) = 1");
            else if (hasStart)
                c.Add($"Plane.time_bounds_lower >= {F(mjdStart)}");
            else if (hasEnd)
                c.Add($"Plane.time_bounds_upper <= {F(mjdEnd)}");
        }

        // Integration time with unit conversion
        AddTimeRangeFromMinMax("Plane.time_exposure", s.IntegrationTimeMin, s.IntegrationTimeMax, s.IntegrationTimeUnit, c, true);

        // Time span
        if (RangeParser.TryParse(s.TimeSpan, out var tsRange))
            AddConvertedRangeClause("Plane.time_bounds_width", tsRange, s.TimeSpanUnit, c,
                (v, u) => UnitConverter.TryConvertToDays(v, u, out var d) ? d : (double?)null);

        // Data release
        if (!string.IsNullOrWhiteSpace(s.DataRelease) && RangeParser.TryParse(s.DataRelease, out var drRange))
            AddDateRangeClause(drRange, c, "Plane.dataRelease");
    }

    private static void AddDateRangeClause(ParsedRange range, List<string> c, string? column = null)
    {
        switch (range.Operand)
        {
            case RangeOperand.Between:
                if (TryParseDateToMJD(range.Value1, out var lo) && TryParseDateToMJD(range.Value2!, out var hi))
                {
                    if (column is not null)
                        c.Add($"{column} >= {F(lo)} AND {column} <= {F(hi)}");
                    else
                        c.Add($"INTERSECTS( INTERVAL( {F(lo)}, {F(hi)} ), Plane.time_bounds_samples ) = 1");
                }
                break;
            case RangeOperand.GreaterThan when TryParseDateToMJD(range.Value1, out var v):
                c.Add($"{column ?? "Plane.time_bounds_lower"} > {F(v)}");
                break;
            case RangeOperand.GreaterThanOrEqual when TryParseDateToMJD(range.Value1, out var v):
                c.Add($"{column ?? "Plane.time_bounds_lower"} >= {F(v)}");
                break;
            case RangeOperand.LessThan when TryParseDateToMJD(range.Value1, out var v):
                c.Add($"{column ?? "Plane.time_bounds_upper"} < {F(v)}");
                break;
            case RangeOperand.LessThanOrEqual when TryParseDateToMJD(range.Value1, out var v):
                c.Add($"{column ?? "Plane.time_bounds_upper"} <= {F(v)}");
                break;
            case RangeOperand.Equals when TryExpandDateToRange(range.Value1, out var eLo, out var eHi):
                if (column is not null)
                    c.Add($"{column} >= {F(eLo)} AND {column} <= {F(eHi)}");
                else
                    c.Add($"INTERSECTS( INTERVAL( {F(eLo)}, {F(eHi)} ), Plane.time_bounds_samples ) = 1");
                break;
        }
    }

    private static void AddTimeRangeFromMinMax(string column, string min, string max, string unit, List<string> c, bool toSeconds)
    {
        if (!string.IsNullOrWhiteSpace(min))
        {
            var (num, inlineUnit) = UnitConverter.ExtractTimeSuffix(min);
            var u = inlineUnit ?? unit;
            if (toSeconds && UnitConverter.TryConvertToSeconds(num, u, out var s))
                c.Add($"{column} >= {F(s)}");
            else if (!toSeconds && UnitConverter.TryConvertToDays(num, u, out var d))
                c.Add($"{column} >= {F(d)}");
        }
        if (!string.IsNullOrWhiteSpace(max))
        {
            var (num, inlineUnit) = UnitConverter.ExtractTimeSuffix(max);
            var u = inlineUnit ?? unit;
            if (toSeconds && UnitConverter.TryConvertToSeconds(num, u, out var s))
                c.Add($"{column} <= {F(s)}");
            else if (!toSeconds && UnitConverter.TryConvertToDays(num, u, out var d))
                c.Add($"{column} <= {F(d)}");
        }
    }

    #endregion

    #region Spectral

    private static void AddSpectralClauses(SearchFormState s, List<string> c)
    {
        // Spectral coverage (overlap semantics)
        if (RangeParser.TryParse(s.SpectralCoverage, out var covRange))
            AddSpectralOverlapClause(covRange, s.SpectralCoverageUnit, c);
        // Legacy wavelength min/max fallback
        else
        {
            // Legacy wavelength min/max fallback
            var hasMin = double.TryParse(s.WavelengthMin, out var wlMin) && wlMin > 0;
            var hasMax = double.TryParse(s.WavelengthMax, out var wlMax) && wlMax > 0;
            if (hasMin && hasMax)
                c.Add($"INTERSECTS( INTERVAL( {F(wlMin)}, {F(wlMax)} ), Plane.energy_bounds_samples ) = 1");
            else if (hasMin)
                c.Add($"Plane.energy_bounds_lower >= {F(wlMin)}");
            else if (hasMax)
                c.Add($"Plane.energy_bounds_upper <= {F(wlMax)}");
        }

        // Spectral sampling
        if (RangeParser.TryParse(s.SpectralSampling, out var samRange))
            AddConvertedRangeClause("Plane.energy_sampleSize", samRange, s.SpectralSamplingUnit, c, ConvertSpectral);

        // Resolving power (dimensionless)
        if (RangeParser.TryParse(s.ResolvingPower, out var rpRange))
            AddNumericRangeClause("Plane.energy_resolvingPower", rpRange, c);

        // Bandpass width
        if (RangeParser.TryParse(s.BandpassWidth, out var bwRange))
            AddConvertedRangeClause("Plane.energy_bounds_width", bwRange, s.BandpassWidthUnit, c, ConvertSpectral);

        // Rest-frame energy
        if (RangeParser.TryParse(s.RestFrameEnergy, out var reRange))
            AddConvertedRangeClause("Plane.energy_restwav", reRange, s.RestFrameEnergyUnit, c, ConvertSpectral);
    }

    private static void AddSpectralOverlapClause(ParsedRange range, string unit, List<string> c)
    {
        if (range.Operand == RangeOperand.Between)
        {
            var (num1, u1) = UnitConverter.ExtractSpectralSuffix(range.Value1);
            var (num2, u2) = UnitConverter.ExtractSpectralSuffix(range.Value2!);
            var effectiveUnit1 = u1 ?? unit;
            var effectiveUnit2 = u2 ?? u1 ?? unit; // inherit from first side

            if (UnitConverter.TryConvertToMetres(num1, effectiveUnit1, out var m1) &&
                UnitConverter.TryConvertToMetres(num2, effectiveUnit2, out var m2))
            {
                var lo = Math.Min(m1, m2);
                var hi = Math.Max(m1, m2);
                // Overlap: query interval overlaps with observation energy bounds
                c.Add($"Plane.energy_bounds_lower <= {F(hi)} AND {F(lo)} <= Plane.energy_bounds_upper");
            }
        }
        else
        {
            var (num, u) = UnitConverter.ExtractSpectralSuffix(range.Value1);
            if (!UnitConverter.TryConvertToMetres(num, u ?? unit, out var m)) return;

            switch (range.Operand)
            {
                case RangeOperand.GreaterThan or RangeOperand.GreaterThanOrEqual:
                    c.Add($"{F(m)} <= Plane.energy_bounds_upper");
                    break;
                case RangeOperand.LessThan or RangeOperand.LessThanOrEqual:
                    c.Add($"Plane.energy_bounds_lower <= {F(m)}");
                    break;
                case RangeOperand.Equals:
                    c.Add($"Plane.energy_bounds_lower <= {F(m)} AND {F(m)} <= Plane.energy_bounds_upper");
                    break;
            }
        }
    }

    private static double? ConvertSpectral(string value, string unit)
    {
        var (num, inlineUnit) = UnitConverter.ExtractSpectralSuffix(value);
        return UnitConverter.TryConvertToMetres(num, inlineUnit ?? unit, out var m) ? m : null;
    }

    #endregion

    #region Data Train + Misc

    private static void AddDataTrainClauses(SearchFormState s, List<string> c)
    {
        AddInClause("Plane.energy_emBand", s.Bands, c);
        AddInClause("Observation.collection", s.Collections, c);
        AddInClause("Observation.instrument_name", s.Instruments, c);
        AddInClause("Plane.energy_bandpassName", s.Filters, c);
        AddInClause("Plane.calibrationLevel", s.CalibrationLevels, c);
        AddInClause("Plane.dataProductType", s.DataProductTypes, c);
        AddInClause("Observation.type", s.ObservationTypes, c);
    }

    private static void AddMiscClauses(SearchFormState s, List<string> c)
    {
        if (!string.IsNullOrWhiteSpace(s.Intent))
            c.Add($"Observation.intent = '{Escape(s.Intent)}'");

        if (s.PublicOnly)
            c.Add("Plane.dataRelease <= GETDATE()");
    }

    #endregion

    #region Helpers

    private static void AddConvertedRangeClause(string column, ParsedRange range, string unit, List<string> c,
        Func<string, string, double?> convert)
    {
        switch (range.Operand)
        {
            case RangeOperand.Between:
            {
                var v1 = convert(range.Value1, unit);
                var v2 = convert(range.Value2!, unit);
                if (v1 is not null && v2 is not null)
                {
                    var lo = Math.Min(v1.Value, v2.Value);
                    var hi = Math.Max(v1.Value, v2.Value);
                    c.Add($"{column} >= {F(lo)} AND {column} <= {F(hi)}");
                }
                break;
            }
            case RangeOperand.GreaterThan when convert(range.Value1, unit) is { } v:
                c.Add($"{column} > {F(v)}"); break;
            case RangeOperand.GreaterThanOrEqual when convert(range.Value1, unit) is { } v:
                c.Add($"{column} >= {F(v)}"); break;
            case RangeOperand.LessThan when convert(range.Value1, unit) is { } v:
                c.Add($"{column} < {F(v)}"); break;
            case RangeOperand.LessThanOrEqual when convert(range.Value1, unit) is { } v:
                c.Add($"{column} <= {F(v)}"); break;
            case RangeOperand.Equals when convert(range.Value1, unit) is { } v:
                c.Add($"{column} = {F(v)}"); break;
        }
    }

    private static void AddNumericRangeClause(string column, ParsedRange range, List<string> c)
    {
        AddConvertedRangeClause(column, range, "", c,
            (v, _) => double.TryParse(v, out var d) ? d : null);
    }

    private static void AddInClause(string column, string commaSeparated, List<string> clauses)
    {
        if (string.IsNullOrWhiteSpace(commaSeparated)) return;
        var values = commaSeparated.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length == 0) return;

        if (values.Length == 1)
            clauses.Add($"{column} = '{Escape(values[0])}'");
        else
        {
            var quoted = string.Join(", ", values.Select(v => $"'{Escape(v)}'"));
            clauses.Add($"{column} IN ( {quoted} )");
        }
    }

    private static string Escape(string value) => value.Replace("'", "''");

    private static string EscapeLike(string value) =>
        Escape(value).Replace("%", "\\%").Replace("_", "\\_");

    /// <summary>Format a double for ADQL using invariant culture.</summary>
    private static string F(double v) => v.ToString("G10", CultureInfo.InvariantCulture);

    #endregion

    #region Date/MJD

    internal static bool TryParseDateToMJD(string dateStr, out double mjd)
    {
        mjd = 0;
        if (string.IsNullOrWhiteSpace(dateStr)) return false;
        if (!DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) return false;
        return TryParseDateToMJD(dt, out mjd);
    }

    internal static bool TryParseDateToMJD(DateTime dt, out double mjd)
    {
        var y = dt.Year;
        var m = dt.Month;
        var d = dt.Day + dt.Hour / 24.0 + dt.Minute / 1440.0 + dt.Second / 86400.0;

        if (m <= 2) { y--; m += 12; }

        var a = y / 100;
        var b = 2 - a + a / 4;
        var jd = Math.Floor(365.25 * (y + 4716)) + Math.Floor(30.6001 * (m + 1)) + d + b - 1524.5;
        mjd = jd - 2400000.5;
        return true;
    }

    /// <summary>
    /// Expand a date string to an MJD range based on granularity.
    /// "2020" → 2020-01-01 .. 2020-12-31, "2020-06" → 2020-06-01 .. 2020-06-30, etc.
    /// </summary>
    internal static bool TryExpandDateToRange(string dateStr, out double mjdLo, out double mjdHi)
    {
        mjdLo = mjdHi = 0;
        if (string.IsNullOrWhiteSpace(dateStr)) return false;
        var s = dateStr.Trim();

        // Year only: "2020"
        if (s.Length == 4 && int.TryParse(s, out var year))
        {
            return TryParseDateToMJD(new DateTime(year, 1, 1), out mjdLo) &&
                   TryParseDateToMJD(new DateTime(year, 12, 31, 23, 59, 59), out mjdHi);
        }

        // Year-month: "2020-06"
        if (s.Length == 7 && DateTime.TryParseExact(s, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var ym))
        {
            var endOfMonth = new DateTime(ym.Year, ym.Month, DateTime.DaysInMonth(ym.Year, ym.Month), 23, 59, 59);
            return TryParseDateToMJD(ym, out mjdLo) && TryParseDateToMJD(endOfMonth, out mjdHi);
        }

        // Full date or longer — treat as single point (no expansion)
        if (TryParseDateToMJD(s, out var mjd))
        {
            mjdLo = mjd;
            mjdHi = mjd;
            return true;
        }

        return false;
    }

    #endregion
}
