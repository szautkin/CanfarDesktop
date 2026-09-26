using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// FITS files made to order for tests: headers card by card, pixels at any BITPIX, a TAN WCS with or
/// without SIP — so a cut can be checked against a file whose every byte the test knows.
/// </summary>
internal static class SyntheticFits
{
    private const int Block = 2880;

    public static string Card(string keyword, string value, string? comment = null)
    {
        var card = $"{keyword,-8}= {value,20}";
        if (comment is not null) card += " / " + comment;
        return card.PadRight(80)[..80];
    }

    public static string Card(string keyword, double value) => Card(keyword, value.ToString("R", CultureInfo.InvariantCulture));

    public static string Card(string keyword, long value) => Card(keyword, value.ToString(CultureInfo.InvariantCulture));

    public static string Text(string keyword, string value) => $"{keyword,-8}= '{value,-8}'".PadRight(80)[..80];

    /// <summary>A card with no value — HISTORY, COMMENT — as the file would have it.</summary>
    public static string Free(string keyword, string text) => $"{keyword,-8}{text}".PadRight(80)[..80];

    /// <summary>One header-and-data unit: the cards, END, padding, then the data padded with zeros.</summary>
    public static byte[] Hdu(IEnumerable<string> cards, byte[]? data = null)
    {
        var header = string.Concat(cards.Select(c => c.PadRight(80)[..80])) + "END".PadRight(80);
        var headerBytes = Encoding.ASCII.GetBytes(header.PadRight(Pad(header.Length)));
        data ??= [];
        var all = new byte[headerBytes.Length + Pad(data.Length)];
        headerBytes.CopyTo(all, 0);
        data.CopyTo(all, headerBytes.Length);
        return all;
    }

    /// <summary>The cards of an image: SIMPLE or XTENSION, BITPIX, NAXIS and its lengths.</summary>
    public static List<string> ImageCards(int bitpix, bool primary, params int[] axes)
    {
        var cards = new List<string>
        {
            primary ? Card("SIMPLE", "T") : Text("XTENSION", "IMAGE"),
            Card("BITPIX", bitpix),
            Card("NAXIS", axes.Length),
        };
        for (var i = 0; i < axes.Length; i++) cards.Add(Card($"NAXIS{i + 1}", axes[i]));
        if (!primary) { cards.Add(Card("PCOUNT", 0)); cards.Add(Card("GCOUNT", 1)); }
        return cards;
    }

    /// <summary>A gnomonic WCS: a CD matrix of <paramref name="scaleDeg"/> per pixel, rotated, east to the left.</summary>
    public static List<string> TanWcs(double crpix1, double crpix2, double ra, double dec, double scaleDeg,
                                      double rotationDeg = 0, bool sip = false)
    {
        var t = rotationDeg * Math.PI / 180;
        var suffix = sip ? "-SIP" : "";
        var cards = new List<string>
        {
            Text("CTYPE1", "RA---TAN" + suffix),
            Text("CTYPE2", "DEC--TAN" + suffix),
            Card("CRPIX1", crpix1), Card("CRPIX2", crpix2),
            Card("CRVAL1", ra), Card("CRVAL2", dec),
            Card("CD1_1", -scaleDeg * Math.Cos(t)), Card("CD1_2", scaleDeg * Math.Sin(t)),
            Card("CD2_1", scaleDeg * Math.Sin(t)), Card("CD2_2", scaleDeg * Math.Cos(t)),
            Text("RADESYS", "ICRS"),
        };
        if (sip)
        {
            // A distortion of a few pixels at the corners of a small frame — enough that ignoring it shows.
            cards.AddRange([Card("A_ORDER", 2), Card("A_2_0", 2e-4), Card("A_0_2", -1e-4), Card("A_1_1", 5e-5),
                            Card("B_ORDER", 2), Card("B_2_0", -1e-4), Card("B_0_2", 3e-4), Card("B_1_1", 2e-5)]);
        }
        return cards;
    }

    /// <summary>Big-endian pixels at <paramref name="bitpix"/>, each the stored value <paramref name="value"/> gives (x, y, plane 0-based).</summary>
    public static byte[] Pixels(int bitpix, int width, int height, Func<int, int, int, double> value, int planes = 1)
    {
        var size = Math.Abs(bitpix) / 8;
        var data = new byte[(long)width * height * planes * size];
        var at = 0;
        for (var z = 0; z < planes; z++)
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++, at += size)
                {
                    var v = value(x, y, z);
                    var span = data.AsSpan(at, size);
                    switch (bitpix)
                    {
                        case 8: span[0] = (byte)v; break;
                        case 16: BinaryPrimitives.WriteInt16BigEndian(span, (short)v); break;
                        case 32: BinaryPrimitives.WriteInt32BigEndian(span, (int)v); break;
                        case 64: BinaryPrimitives.WriteInt64BigEndian(span, (long)v); break;
                        case -32: BinaryPrimitives.WriteSingleBigEndian(span, (float)v); break;
                        default: BinaryPrimitives.WriteDoubleBigEndian(span, v); break;
                    }
                }
        return data;
    }

    /// <summary>Write the HDUs one after another as a file in <paramref name="directory"/>.</summary>
    public static string Write(string directory, string name, params byte[][] hdus)
    {
        var path = Path.Combine(directory, name);
        using var file = File.Create(path);
        foreach (var hdu in hdus) file.Write(hdu);
        return path;
    }

    private static int Pad(int length) => (length + Block - 1) / Block * Block;
}
