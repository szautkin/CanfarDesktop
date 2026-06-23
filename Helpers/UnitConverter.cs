using System.Globalization;
using System.Text.RegularExpressions;

namespace CanfarDesktop.Helpers;

public static class UnitConverter
{
    // Physical constants
    private const double SpeedOfLight = 299792458.0; // m/s
    private const double PlanckConstant = 6.62607015e-34; // J·s
    private const double EvToJoules = 1.602176634e-19; // J/eV

    // Unit arrays for UI ComboBoxes
    public static readonly string[] SpectralUnits =
        ["m", "cm", "mm", "\u00b5m", "nm", "\u00c5", "Hz", "kHz", "MHz", "GHz", "eV", "keV", "MeV", "GeV"];

    public static readonly string[] TimeUnits = ["s", "m", "h", "d", "y"];

    public static readonly string[] PixelScaleUnits = ["arcsec", "arcmin", "deg"];

    // Spectral unit regex — longest match first
    private static readonly Regex SpectralSuffixRegex = new(
        @"(GHz|MHz|kHz|GeV|MeV|keV|nm|um|\u00b5m|mm|cm|Hz|eV|\u00c5|A|m)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TimeSuffixRegex = new(
        @"([smhdy])$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PixelScaleSuffixRegex = new(
        @"(arcmin|arcsec|deg)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Extract trailing unit suffix from a raw value string.
    /// Returns (numericPart, unit) or (input, null) if no unit found.
    /// </summary>
    public static (string numeric, string? unit) ExtractSpectralSuffix(string raw)
    {
        var match = SpectralSuffixRegex.Match(raw.Trim());
        if (!match.Success) return (raw.Trim(), null);
        return (raw[..match.Index].Trim(), match.Value.ToLower());
    }

    public static (string numeric, string? unit) ExtractTimeSuffix(string raw)
    {
        var match = TimeSuffixRegex.Match(raw.Trim());
        if (!match.Success) return (raw.Trim(), null);
        return (raw[..match.Index].Trim(), match.Value.ToLower());
    }

    public static (string numeric, string? unit) ExtractPixelScaleSuffix(string raw)
    {
        var match = PixelScaleSuffixRegex.Match(raw.Trim());
        if (!match.Success) return (raw.Trim(), null);
        return (raw[..match.Index].Trim(), match.Value.ToLower());
    }

    /// <summary>
    /// Convert a value with spectral unit to metres.
    /// Supports wavelength (direct), frequency (c/f), energy (hc/E).
    /// </summary>
    public static bool TryConvertToMetres(string numericValue, string unit, out double metres)
    {
        metres = 0;
        if (!double.TryParse(numericValue, out var val) || val <= 0) return false;

        var u = unit.ToLower()
            .Replace("\u00b5", "u")  // µ → u
            .Replace("\u00c5", "a")  // Å → a
            .Replace("\u00e5", "a"); // å → a (lowercased Å)

        // Wavelength → metres (direct multiplication)
        double? factor = u switch
        {
            "m" => 1.0,
            "cm" => 1e-2,
            "mm" => 1e-3,
            "um" => 1e-6,
            "nm" => 1e-9,
            "a" => 1e-10,
            _ => null
        };
        if (factor is not null)
        {
            metres = val * factor.Value;
            return true;
        }

        // Frequency → metres (λ = c / f)
        double? freqFactor = u switch
        {
            "hz" => 1.0,
            "khz" => 1e3,
            "mhz" => 1e6,
            "ghz" => 1e9,
            _ => null
        };
        if (freqFactor is not null)
        {
            var freqHz = val * freqFactor.Value;
            metres = SpeedOfLight / freqHz;
            return true;
        }

        // Energy → metres (λ = hc / E)
        double? evFactor = u switch
        {
            "ev" => 1.0,
            "kev" => 1e3,
            "mev" => 1e6,
            "gev" => 1e9,
            _ => null
        };
        if (evFactor is not null)
        {
            var energyJ = val * evFactor.Value * EvToJoules;
            metres = PlanckConstant * SpeedOfLight / energyJ;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Convert time value to seconds.
    /// </summary>
    public static bool TryConvertToSeconds(string numericValue, string unit, out double seconds)
    {
        seconds = 0;
        if (!double.TryParse(numericValue, out var val)) return false;

        double? factor = unit.ToLower() switch
        {
            "s" => 1.0,
            "m" => 60.0,
            "h" => 3600.0,
            "d" => 86400.0,
            "y" => 365.25 * 86400.0,
            _ => null
        };

        if (factor is null) return false;
        seconds = val * factor.Value;
        return true;
    }

    /// <summary>
    /// Convert time value to days.
    /// </summary>
    public static bool TryConvertToDays(string numericValue, string unit, out double days)
    {
        days = 0;
        if (!double.TryParse(numericValue, out var val)) return false;

        double? factor = unit.ToLower() switch
        {
            "s" => 1.0 / 86400.0,
            "m" => 1.0 / 1440.0,
            "h" => 1.0 / 24.0,
            "d" => 1.0,
            "y" => 365.25,
            _ => null
        };

        if (factor is null) return false;
        days = val * factor.Value;
        return true;
    }

    /// <summary>
    /// Convert pixel scale value to degrees.
    /// </summary>
    public static bool TryConvertToDegrees(string numericValue, string unit, out double degrees)
    {
        degrees = 0;
        if (!double.TryParse(numericValue, out var val)) return false;

        double? factor = unit.ToLower() switch
        {
            "arcsec" => 1.0 / 3600.0,
            "arcmin" => 1.0 / 60.0,
            "deg" => 1.0,
            _ => null
        };

        if (factor is null) return false;
        degrees = val * factor.Value;
        return true;
    }

    /// <summary>
    /// Returns true if the unit is frequency or energy (inverse relationship to wavelength).
    /// When converting a range, bounds must be swapped.
    /// </summary>
    public static bool IsInverseUnit(string unit)
    {
        var u = unit.ToLower();
        return u is "hz" or "khz" or "mhz" or "ghz" or "ev" or "kev" or "mev" or "gev";
    }

    // ── Display-side unit rendering (search-results unit menus) ───────────────
    // macOS-faithful legacy CGS constants (match CADC CCDA convertUtils) so a TAP row renders to the
    // SAME numbers as the reference client for any chosen unit. Kept separate from the SI constants
    // above (used by the search-form TryConvertTo* path) so search behaviour is unchanged.
    private const double CgsSpeedOfLight = 2.997925e8; // m/s
    private const double CgsPlanck = 6.6262e-27;       // erg·s
    private const double CgsErgPerEv = 1.602192e-12;   // erg/eV

    private enum SpectralDimension { Wavelength, Frequency, Energy }

    private sealed record SpectralUnitInfo(string Id, string Label, SpectralDimension Dimension, double FactorFromBase);

    private static readonly SpectralUnitInfo[] SpectralUnitTable =
    {
        new("m",  "m",        SpectralDimension.Wavelength, 1),
        new("cm", "cm",       SpectralDimension.Wavelength, 1e-2),
        new("mm", "mm",       SpectralDimension.Wavelength, 1e-3),
        new("um", "µm",  SpectralDimension.Wavelength, 1e-6),
        new("nm", "nm",       SpectralDimension.Wavelength, 1e-9),
        new("a",  "Å",   SpectralDimension.Wavelength, 1e-10),
        new("hz",  "Hz",      SpectralDimension.Frequency, 1),
        new("khz", "kHz",     SpectralDimension.Frequency, 1e3),
        new("mhz", "MHz",     SpectralDimension.Frequency, 1e6),
        new("ghz", "GHz",     SpectralDimension.Frequency, 1e9),
        new("ev",  "eV",      SpectralDimension.Energy, 1),
        new("kev", "keV",     SpectralDimension.Energy, 1e3),
        new("mev", "MeV",     SpectralDimension.Energy, 1e6),
        new("gev", "GeV",     SpectralDimension.Energy, 1e9),
    };

    /// <summary>The ordered (id, label) spectral unit choices for a column unit menu.</summary>
    public static IReadOnlyList<(string Id, string Label)> SpectralUnitChoices
        => SpectralUnitTable.Select(u => (u.Id, u.Label)).ToList();

    /// <summary>
    /// Convert a wavelength in metres to <paramref name="unitId"/> (cross-dimension via c and hc).
    /// Null on non-positive / non-finite input. 1-to-1 with macOS SpectralConverter.
    /// </summary>
    public static double? ConvertSpectral(double metres, string unitId)
    {
        if (!double.IsFinite(metres) || metres <= 0) return null;
        var unit = Array.Find(SpectralUnitTable, u => u.Id == unitId.ToLowerInvariant());
        if (unit is null) return null;
        return unit.Dimension switch
        {
            SpectralDimension.Wavelength => metres / unit.FactorFromBase,
            SpectralDimension.Frequency => CgsSpeedOfLight / metres / unit.FactorFromBase,
            SpectralDimension.Energy => CgsPlanck * CgsSpeedOfLight / (CgsErgPerEv * metres) / unit.FactorFromBase,
            _ => null,
        };
    }

    /// <summary>Render a metres-stored wavelength as "value label" in the chosen spectral unit; raw on failure.</summary>
    public static string FormatSpectral(string raw, string unitId)
    {
        var metres = FiniteDouble(raw);
        if (metres is null) return raw.Trim();
        var value = ConvertSpectral(metres.Value, unitId);
        if (value is null) return raw.Trim();
        var label = Array.Find(SpectralUnitTable, u => u.Id == unitId.ToLowerInvariant())?.Label ?? unitId;
        return $"{SpectralValueString(value.Value)} {label}";
    }

    // macOS SpectralFormatter precision ladder: >=100 → 1dp, >=1 → 2dp, >=0.001 → 3dp, else 4 sig figs.
    private static string SpectralValueString(double v)
    {
        var mag = Math.Abs(v);
        if (mag == 0) return "0";
        if (mag >= 100) return v.ToString("F1", CultureInfo.InvariantCulture);
        if (mag >= 1) return v.ToString("F2", CultureInfo.InvariantCulture);
        if (mag >= 0.001) return v.ToString("F3", CultureInfo.InvariantCulture);
        return v.ToString("G4", CultureInfo.InvariantCulture);
    }

    /// <summary>Render seconds as "value label" in the chosen duration unit (seconds/minutes/hours/days).</summary>
    public static string FormatDuration(string raw, string unitId)
    {
        var seconds = FiniteDouble(raw);
        if (seconds is null) return raw.Trim();
        var (factor, label) = unitId.ToLowerInvariant() switch
        {
            "minutes" => (60.0, "m"),
            "hours" => (3600.0, "h"),
            "days" => (86400.0, "d"),
            _ => (1.0, "s"),
        };
        return $"{(seconds.Value / factor).ToString("F3", CultureInfo.InvariantCulture)} {label}";
    }

    /// <summary>Render degrees as "value label" in the chosen angle unit (mas/arcsec/arcmin/deg).</summary>
    public static string FormatAngle(string raw, string unitId)
    {
        var degrees = FiniteDouble(raw);
        if (degrees is null) return raw.Trim();
        var (factor, label) = unitId.ToLowerInvariant() switch
        {
            "milliarcseconds" => (3600000.0, "mas"),
            "arcminutes" => (60.0, "′"),
            "degrees" => (1.0, "°"),
            _ => (3600.0, "″"), // arcseconds
        };
        return $"{AdaptivePrecision(degrees.Value * factor)} {label}";
    }

    /// <summary>Render square-degrees as "value label" in the chosen area unit.</summary>
    public static string FormatArea(string raw, string unitId)
    {
        var sqDeg = FiniteDouble(raw);
        if (sqDeg is null) return raw.Trim();
        var (factor, label) = unitId.ToLowerInvariant() switch
        {
            "sq_arcsec" => (12960000.0, "sq arcsec"),
            "sq_arcmin" => (3600.0, "sq arcmin"),
            _ => (1.0, "sq deg"),
        };
        return $"{AdaptivePrecision(sqDeg.Value * factor)} {label}";
    }

    /// <summary>macOS adaptivePrecisionString: 6dp below 0.001 (non-zero), else 3dp.</summary>
    public static string AdaptivePrecision(double v)
    {
        var mag = Math.Abs(v);
        return mag != 0 && mag < 0.001
            ? v.ToString("F6", CultureInfo.InvariantCulture)
            : v.ToString("F3", CultureInfo.InvariantCulture);
    }

    private static double? FiniteDouble(string raw)
        => double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v)
            ? v : null;
}
