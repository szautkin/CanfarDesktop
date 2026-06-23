using System.Globalization;

namespace CanfarDesktop.Helpers;

/// <summary>
/// Parses sexagesimal celestial coordinates (RA in hours, Dec in degrees) to decimal degrees.
/// Accepts ':' or whitespace as separators (e.g. "10:00:00", "10 00 00", "-30:15:00").
/// </summary>
public static class Sexagesimal
{
    private static readonly char[] Separators = { ':', ' ' };

    /// <summary>
    /// Parse sexagesimal RA (HH:MM:SS or HH MM SS) to degrees.
    /// Validates h in [0,24), m in [0,60), s in [0,60). Returns null on malformed input.
    /// </summary>
    public static double? ParseRa(string? str)
    {
        if (string.IsNullOrWhiteSpace(str)) return null;
        var parts = str.Trim().Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        if (!TryParse(parts[0], out var h)) return null;
        var m = parts.Length > 1 ? Parse(parts[1]) : 0;
        var s = parts.Length > 2 ? Parse(parts[2]) : 0;
        if (h < 0 || h >= 24 || m < 0 || m >= 60 || s < 0 || s >= 60) return null;
        return (h + m / 60.0 + s / 3600.0) * 15.0; // hours → degrees
    }

    /// <summary>
    /// Parse sexagesimal Dec (±DD:MM:SS or ±DD MM SS) to degrees.
    /// Validates d in [0,90], m in [0,60), s in [0,60). Returns null on malformed input.
    /// </summary>
    public static double? ParseDec(string? str)
    {
        if (string.IsNullOrWhiteSpace(str)) return null;
        var trimmed = str.Trim();
        var sign = trimmed.StartsWith('-') ? -1.0 : 1.0;
        var cleaned = trimmed.Replace("+", "").Replace("-", "");
        var parts = cleaned.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        if (!TryParse(parts[0], out var d)) return null;
        var m = parts.Length > 1 ? Parse(parts[1]) : 0;
        var s = parts.Length > 2 ? Parse(parts[2]) : 0;
        if (d < 0 || d > 90 || m < 0 || m >= 60 || s < 0 || s >= 60) return null;
        return sign * (d + m / 60.0 + s / 3600.0);
    }

    private static bool TryParse(string s, out double value)
        => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static double Parse(string s) => TryParse(s, out var v) ? v : 0;
}
