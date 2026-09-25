using System.Globalization;

namespace CanfarDesktop.Helpers;

/// <summary>
/// What "Copy coordinates" puts on the clipboard.
///
/// <para>The clipboard is a seam between two parts of this same app: the obvious thing to do with a
/// copied position is paste it into the Search box. So the format is not a presentation choice — it
/// is whatever <see cref="ADQLBuilder.TryParseCoordinatePair"/> accepts, which is a whitespace-
/// separated pair in decimal degrees or colon-delimited sexagesimal.</para>
///
/// <para>The first version of this copied the readout's own glyphs, <c>16h00m00.00s +48°00'00.0"</c>,
/// followed by the degrees in brackets. Nothing in the app can read that: the parser splits on
/// whitespace and wants exactly two tokens, the h/m/s and °/'/" forms parse as neither a number nor a
/// colon sexagesimal, and the bracketed degrees added two more tokens. Pasted into our own search it
/// fell through every coordinate branch to the target-NAME match and searched for an observation
/// called "16h00m00.00s". Silently — a position that finds nothing looks the same as a position with
/// nothing there.</para>
///
/// <para>Sexagesimal with colons rather than decimal degrees, because it parses here AND in Simbad,
/// NED, DS9 and CARTA, and it is the form an astronomer reads. Degrees are still available in full
/// precision from the export, which carries both.</para>
/// </summary>
public static class MarkClipboard
{
    /// <summary>
    /// A sky position, as our own search box parses it: <c>HH:MM:SS.cc ±DD:MM:SS.s</c>.
    ///
    /// One space, two tokens, nothing else. A third token would be read as a search radius, so
    /// helpfully appending the degrees would turn a position into a cone of that many degrees.
    /// </summary>
    public static string Sky(double raDeg, double decDeg)
        => $"{Sexagesimal.FormatRaHms(raDeg)} {Sexagesimal.FormatDecDms(decDeg)}";

    /// <summary>
    /// An image pixel, labelled as one.
    ///
    /// No sky, so nothing can search it, and the unit is stated so it is not mistaken for degrees by
    /// whoever reads it next.
    /// </summary>
    public static string ImagePixel(double x, double y)
        => string.Format(CultureInfo.InvariantCulture, "{0:0.##}, {1:0.##} px", x, y);

    /// <summary>
    /// A cube voxel: x, y and the channel, named.
    ///
    /// The channel is the part somebody is most likely to have come looking for, and a cube position
    /// without it is two thirds of an answer.
    /// </summary>
    public static string Voxel(double x, double y, double channel)
        => string.Format(
            CultureInfo.InvariantCulture, "x={0:0.##}, y={1:0.##}, channel={2:0.##}", x, y, channel);
}
