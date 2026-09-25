using System.Globalization;
using System.Text;

namespace CanfarDesktop.Helpers;

/// <summary>
/// The same marks as a DS9 region file.
///
/// <para>JSON carries everything, and nothing reads it. A <c>.reg</c> file carries less and every
/// tool in the field opens it — DS9, CARTA, pyregion, the reduction script somebody already has. It
/// is the difference between marks that can be cited and marks that can be USED.</para>
///
/// <para>Written in fk5 degrees when the image has a WCS, so the regions land on any image of the
/// same field rather than only on this one. Without a WCS they are written in image pixels, which is
/// the honest fallback and still opens.</para>
/// </summary>
public static class Ds9Regions
{
    /// <summary>
    /// Render the document.
    ///
    /// <para>A mark with no size becomes a point rather than being dropped: a position someone
    /// deliberately marked is the most citable thing in the file.</para>
    /// </summary>
    public static string Write(MarkExport.Document document)
    {
        var sky = document.Source.Wcs is { IsValid: true };
        var text = new StringBuilder();

        text.Append("# Region file format: DS9 version 4.1\n");
        text.Append(CultureInfo.InvariantCulture, $"# Exported by Verbinal {document.AppVersion} at {document.ExportedAt}\n");
        text.Append(CultureInfo.InvariantCulture, $"# {document.Source.FileName}");

        if (document.Source.HduName is { Length: > 0 } hdu)
            text.Append(CultureInfo.InvariantCulture, $" [{hdu}]");
        text.Append('\n');

        if (document.Observation?.PublisherId is { Length: > 0 } id)
            text.Append(CultureInfo.InvariantCulture, $"# {id}\n");

        text.Append("global width=1\n");
        text.Append(sky ? "fk5\n" : "image\n");

        foreach (var mark in document.Marks)
        {
            if (Shape(mark, sky) is not { Length: > 0 } shape) continue;

            text.Append(shape);

            // The label, and the colour it was drawn in — a region file that came back in a different
            // colour from the figure beside it is a small thing that makes people doubt both.
            text.Append(CultureInfo.InvariantCulture, $" # color={Colour(mark.Colour)}");
            if (mark.Text is { Length: > 0 } label)
                text.Append(CultureInfo.InvariantCulture, $" text={{{Sanitise(label)}}}");

            text.Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    /// One region line, or empty when the mark cannot be placed in the chosen coordinate system.
    ///
    /// Sizes go out in arcseconds for a sky file and pixels for an image one — DS9's own convention,
    /// and the reason the export carries both.
    /// </summary>
    private static string Shape(MarkExport.ExportedMark mark, bool sky)
    {
        double x, y;
        if (sky)
        {
            if (mark.RaDeg is not { } ra || mark.DecDeg is not { } dec) return "";
            x = ra;
            y = dec;
        }
        else
        {
            if (mark.PixelX is not { } px || mark.PixelY is not { } py) return "";

            // DS9 image coordinates are 1-based; the app counts display pixels from zero.
            x = px + 1;
            y = py + 1;
        }

        var size = sky ? mark.HalfWidthArcsec : mark.HalfWidthPixels;
        var unit = sky ? "\"" : "";

        return mark.Kind switch
        {
            "circle" when size is { } r => Line("circle", x, y, $"{Number(r)}{unit}"),
            "rect" when size is { } h =>
                Line("box", x, y, $"{Number(h * 2)}{unit}", $"{Number(h * 2)}{unit}", "0"),

            // A callout's shape is a ring at the subject; the leader and its label are the app's
            // drawing, not a region. A text mark is a position with words, which DS9 has exactly.
            "callout" when size is { } r => Line("circle", x, y, $"{Number(r)}{unit}"),
            "text" => Line("point", x, y),

            _ => Line("point", x, y),
        };
    }

    private static string Line(string shape, double x, double y, params string[] rest)
    {
        var parts = new List<string> { Number(x), Number(y) };
        parts.AddRange(rest);
        return $"{shape}({string.Join(",", parts)})";
    }

    /// <summary>Enough digits for a sub-arcsecond position, and no exponent for DS9 to trip over.</summary>
    private static string Number(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);

    /// <summary>DS9 takes a name or a hex triplet; the app's colours are hex already.</summary>
    private static string Colour(string? hex)
        => string.IsNullOrWhiteSpace(hex) ? "green" : hex.Trim();

    /// <summary>
    /// A label cannot carry the braces that delimit it, or a newline.
    ///
    /// Replaced rather than escaped: DS9's parser has no escape inside a brace-delimited string, so a
    /// brace in a mark's words would end the label early and leave the rest as broken syntax.
    /// </summary>
    private static string Sanitise(string text)
        => text.Replace('{', '(').Replace('}', ')').Replace('\r', ' ').Replace('\n', ' ').Trim();
}
