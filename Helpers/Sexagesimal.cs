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

    // ── Formatting (decimal degrees → sexagesimal) ───────────────────────────

    /// <summary>
    /// Format decimal degrees as sexagesimal RA <c>HH:MM:SS.cc</c> (hours, 2-decimal seconds, no
    /// sign). Integer-centisecond arithmetic wrapped to [0,24)h — 1-to-1 with the macOS HMSFormatter
    /// (so 359.999999° → 23:59:59.99, never 24:00:00.00).
    /// </summary>
    public static string FormatRaHms(double deg)
    {
        var hours = (deg / 15.0) % 24.0;
        if (hours < 0) hours += 24.0;
        const long dayCs = 24L * 3600 * 100;
        var totalCs = (long)Math.Round(hours * 3600 * 100, MidpointRounding.AwayFromZero);
        totalCs = ((totalCs % dayCs) + dayCs) % dayCs;
        var h = totalCs / 360000;
        var m = (totalCs / 6000) % 60;
        var s = (totalCs / 100) % 60;
        var cs = totalCs % 100;
        return string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}:{2:D2}.{3:D2}", h, m, s, cs);
    }

    /// <summary>RA formatter over a raw string; returns the trimmed raw unchanged when unparseable.</summary>
    public static string FormatRaHms(string? raw)
    {
        var trimmed = raw?.Trim() ?? string.Empty;
        return TryParse(trimmed, out var v) && double.IsFinite(v) ? FormatRaHms(v) : trimmed;
    }

    /// <summary>
    /// Format decimal degrees as sexagesimal Dec <c>±DD:MM:SS.d</c> (always-signed, 1-decimal
    /// seconds). Integer deci-arcsecond arithmetic — 1-to-1 with the macOS DMSFormatter. The double
    /// overload assumes an in-range value; callers use <see cref="FormatDecDms(string)"/> for the
    /// macOS out-of-range passthrough.
    /// </summary>
    public static string FormatDecDms(double deg)
    {
        var sign = deg < 0 ? "-" : "+"; // always shown (+ for >= 0)
        var totalDs = (long)Math.Round(Math.Abs(deg) * 3600 * 10, MidpointRounding.AwayFromZero);
        var d = totalDs / 36000;
        var m = (totalDs / 600) % 60;
        var s = (totalDs / 10) % 60;
        var ds = totalDs % 10;
        return string.Format(CultureInfo.InvariantCulture, "{0}{1:D2}:{2:D2}:{3:D2}.{4}", sign, d, m, s, ds);
    }

    /// <summary>Dec formatter over a raw string; passthrough when unparseable OR outside [-90, 90].</summary>
    public static string FormatDecDms(string? raw)
    {
        var trimmed = raw?.Trim() ?? string.Empty;
        return TryParse(trimmed, out var v) && double.IsFinite(v) && v is >= -90.0 and <= 90.0
            ? FormatDecDms(v)
            : trimmed;
    }

    private static bool TryParse(string s, out double value)
        => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static double Parse(string s) => TryParse(s, out var v) ? v : 0;
}
