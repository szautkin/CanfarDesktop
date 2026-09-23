using System.Globalization;

namespace CanfarDesktop.Helpers;

/// <summary>
/// The one way this app writes an instant into a file or an answer: UTC to the second, ISO 8601 —
/// <c>2026-09-22T21:09:00Z</c>.
///
/// <para>It was spelled out by hand in a dozen places, and six of them left out the invariant culture.
/// In a custom format string <c>:</c> is not a colon — it is the culture's TIME SEPARATOR — so on a
/// machine whose regional format uses a dot (Finnish, Danish) those stores wrote <c>21.09.00</c>: not
/// ISO, and not something the next reader can parse back.</para>
/// </summary>
public static class IsoTime
{
    public const string Format = "yyyy-MM-ddTHH:mm:ssZ";

    /// <summary>Now, in UTC.</summary>
    public static string Now() => Of(DateTimeOffset.UtcNow);

    /// <summary>An instant, converted to UTC first, so the trailing Z is true whatever offset it carried.</summary>
    public static string Of(DateTimeOffset at) => at.UtcDateTime.ToString(Format, CultureInfo.InvariantCulture);

    /// <summary>A time the caller already holds in UTC. Not converted: the caller vouches for the Z.</summary>
    public static string OfUtc(DateTime utc) => utc.ToString(Format, CultureInfo.InvariantCulture);
}
