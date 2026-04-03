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
}
