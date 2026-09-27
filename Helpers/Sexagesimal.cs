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

    /// <summary>
    /// An angle as a person types it: decimal degrees ("10.68", or "10,68" from a French keyboard,
    /// through <see cref="NumberInput.TryParseUser"/>), or sexagesimal with ':' or spaces — RA in
    /// hours, Dec in degrees. The one reading of a typed coordinate, for the search's target and the
    /// cutout editor's fields alike.
    /// </summary>
    public static bool TryParseAngle(string? text, bool isRa, out double degrees)
    {
        var t = text?.Trim() ?? string.Empty;
        if (NumberInput.TryParseUser(t, out degrees)) return double.IsFinite(degrees);

        var parsed = isRa ? ParseRa(t) : ParseDec(t);
        degrees = parsed ?? 0;
        return parsed is not null;
    }

    // ── Decomposition (the one carry-correct split every presentation formats from) ──────────

    /// <summary>
    /// A sexagesimal value already carried: <c>SS</c> is never 60, <c>MM</c> is never 60, and the
    /// fractional part is an integer in the requested number of decimals.
    ///
    /// <see cref="Sign"/> is -1 or +1 and is meaningful only for a declination; an RA is wrapped into
    /// range instead of signed.
    /// </summary>
    public readonly record struct SexagesimalParts(int Sign, int Units, int Minutes, int Seconds, int Fraction);

    /// <summary>
    /// Split RA into carry-corrected hours/minutes/seconds, wrapped into [0,24)h.
    ///
    /// <para>The arithmetic is integer, in the smallest unit the requested precision needs, which is
    /// what makes the carry exact. Doing it in floating point and truncating gives 359.999999° as
    /// <c>23h59m60.00s</c> — sixty seconds, which is not a time — and that is what this replaced in
    /// three separate places.</para>
    /// </summary>
    public static SexagesimalParts SplitRa(double deg, int decimals)
    {
        var scale = Pow10(decimals);
        var hours = (deg / 15.0) % 24.0;
        if (hours < 0) hours += 24.0;

        var perDay = 24L * 3600 * scale;
        var total = (long)Math.Round(hours * 3600 * scale, MidpointRounding.AwayFromZero);
        total = ((total % perDay) + perDay) % perDay;   // the wrap, after rounding rather than before

        return Decompose(1, total, scale);
    }

    /// <summary>
    /// Split Dec into carry-corrected sign/degrees/minutes/seconds. Not wrapped: a declination out of
    /// [-90,90] is a caller's problem to notice, and silently folding it would hide theirs.
    /// </summary>
    public static SexagesimalParts SplitDec(double deg, int decimals)
    {
        var scale = Pow10(decimals);
        var sign = deg < 0 ? -1 : 1;
        var total = (long)Math.Round(Math.Abs(deg) * 3600 * scale, MidpointRounding.AwayFromZero);
        return Decompose(sign, total, scale);
    }

    private static SexagesimalParts Decompose(int sign, long total, long scale)
        => new(sign,
            (int)(total / (3600 * scale)),
            (int)(total / (60 * scale) % 60),
            (int)(total / scale % 60),
            (int)(total % scale));

    private static long Pow10(int decimals)
    {
        if (decimals is < 0 or > 6)
            throw new ArgumentOutOfRangeException(nameof(decimals), decimals, "0 to 6 decimals");
        long scale = 1;
        for (var i = 0; i < decimals; i++) scale *= 10;
        return scale;
    }

    // ── Formatting (decimal degrees → sexagesimal) ───────────────────────────

    /// <summary>
    /// Format decimal degrees as sexagesimal RA <c>HH:MM:SS.cc</c> (hours, 2-decimal seconds, no
    /// sign). Integer-centisecond arithmetic wrapped to [0,24)h — 1-to-1 with the macOS HMSFormatter,
    /// so a value that rounds up past the last centisecond of the day reads 00:00:00.00 rather than
    /// 24:00:00.00 (and never 23:59:60.00).
    /// </summary>
    public static string FormatRaHms(double deg)
    {
        var p = SplitRa(deg, 2);
        return string.Format(CultureInfo.InvariantCulture,
            "{0:D2}:{1:D2}:{2:D2}.{3:D2}", p.Units, p.Minutes, p.Seconds, p.Fraction);
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
        var p = SplitDec(deg, 1);
        return string.Format(CultureInfo.InvariantCulture,
            "{0}{1:D2}:{2:D2}:{3:D2}.{4}", p.Sign < 0 ? "-" : "+", p.Units, p.Minutes, p.Seconds, p.Fraction);
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
