using System.Globalization;

namespace CanfarDesktop.Helpers;

/// <summary>
/// Culture-safe numeric parsing, in two strictness levels:
/// <list type="bullet">
/// <item><see cref="TryParseWire"/> — strict invariant, for machine data (TAP CSV, FITS cards, API
/// payloads) that is dot-decimal by specification regardless of the user's locale.</item>
/// <item><see cref="TryParseUser"/> — invariant plus comma tolerance, for text the USER typed
/// (radii, wavelengths, unit values): a French keyboard's "0,5" and the universal "0.5" both work.
/// Never uses CurrentCulture, so behavior is identical on every machine.</item>
/// </list>
/// </summary>
public static class NumberInput
{
    public static bool TryParseWire(string? s, out double value)
        => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    public static bool TryParseUser(string? s, out double value)
    {
        if (TryParseWire(s, out value)) return true;
        // Comma decimal — but not a thousands-style "1,234,5" mess: only a single comma qualifies.
        var t = s?.Trim();
        if (t is null || t.Count(c => c == ',') != 1) { value = 0; return false; }
        return TryParseWire(t.Replace(',', '.'), out value);
    }
}
