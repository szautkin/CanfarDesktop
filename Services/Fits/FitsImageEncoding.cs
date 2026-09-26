using CanfarDesktop.Models.Fits;

namespace CanfarDesktop.Services.Fits;

/// <summary>
/// An image's pixels, a row at a time, as a plain image HDU stores them: big-endian, at the plain
/// image's BITPIX, unscaled. What a cut copies — so the pixels written are the pixels that were there.
/// </summary>
public interface IHduPixels : IDisposable
{
    int BytesPerPixel { get; }

    /// <summary>
    /// Fill <paramref name="destination"/> with the pixels of one row from column <paramref name="x0"/>
    /// (0-based). <paramref name="row"/> counts rows over every axis after the first: row y of plane z
    /// of an image NAXIS2 rows high is z × NAXIS2 + y.
    /// </summary>
    void ReadRow(long row, long x0, Span<byte> destination);
}

/// <summary>
/// How an HDU keeps its image in the file: as a plain array, or tile-compressed in a binary table. What
/// a cut asks of it is the same either way — the image's header as a plain image, and its rows — so a
/// cut never needs to know which it has.
/// </summary>
public interface IFitsImageEncoding
{
    /// <summary>Whether this is how the HDU keeps its image.</summary>
    bool Recognises(FitsHeader header);

    /// <summary>Why its image cannot be read this way, when it cannot; null when it can.</summary>
    string? Refusal(FitsHeader header);

    /// <summary>The image's header as a plain image HDU would have it: what a cut's header starts from.</summary>
    FitsHeaderCards ImageCards(FitsHduLayout hdu);

    /// <summary>Its pixels, from a seekable stream over the whole file. The stream stays the caller's.</summary>
    IHduPixels Open(Stream file, FitsHduLayout hdu);
}

/// <summary>The encodings a FITS image can come in, the first that recognises an HDU being its own.</summary>
public static class FitsImageEncodings
{
    private static readonly IFitsImageEncoding[] All = [new TileCompressedImage(), new PlainImage()];

    /// <summary>How this HDU keeps an image, or null when it holds none — a table, or only a header.</summary>
    public static IFitsImageEncoding? For(FitsHeader header) => All.FirstOrDefault(e => e.Recognises(header));
}

/// <summary>A plain image: the primary array, or an IMAGE extension, its pixels one after another.</summary>
public sealed class PlainImage : IFitsImageEncoding
{
    public bool Recognises(FitsHeader header)
    {
        if (header.IsTileCompressed || header.NAxis < 2 || header.NAxis1 <= 0 || header.NAxis2 <= 0) return false;
        var xtension = header.GetString("XTENSION");
        return xtension is null || xtension.Equals("IMAGE", StringComparison.OrdinalIgnoreCase);
    }

    public string? Refusal(FitsHeader header) => header.BitPix is 8 or 16 or 32 or 64 or -32 or -64
        ? null
        : string.Format(Cutouts.CutoutRules.T("Cutout_LocalBadBitpix", "The downloaded file's pixels are stored in a way FITS does not define (BITPIX {0})."), header.BitPix);

    public FitsHeaderCards ImageCards(FitsHduLayout hdu) => new(hdu.RawCards);

    public IHduPixels Open(Stream file, FitsHduLayout hdu) => new Rows(file, hdu);

    /// <summary>Reads each row straight from where it lies: a seek and one read, whatever the file's size.</summary>
    private sealed class Rows(Stream file, FitsHduLayout hdu) : IHduPixels
    {
        private readonly long _width = hdu.Header.NAxis1;

        public int BytesPerPixel { get; } = Math.Abs(hdu.Header.BitPix) / 8;

        public void ReadRow(long row, long x0, Span<byte> destination)
        {
            file.Position = hdu.DataStart + (row * _width + x0) * BytesPerPixel;
            file.ReadExactly(destination);
        }

        public void Dispose() { /* the stream is the caller's */ }
    }
}

/// <summary>
/// An image tile-compressed into a binary table, as fpack writes it (the ZIMAGE convention). Recognised
/// so that a file of them is refused with the reason, rather than mistaken for a file with no image.
/// </summary>
public sealed class TileCompressedImage : IFitsImageEncoding
{
    public bool Recognises(FitsHeader header) => header.IsTileCompressed;

    public string? Refusal(FitsHeader header)
        => string.Format(Cutouts.CutoutRules.T("Cutout_LocalCompressed",
            "The downloaded file is tile-compressed ({0}), which the app cannot cut yet; funpack makes a plain .fits of it that can be."),
            header.GetString("ZCMPTYPE") ?? "?");

    public FitsHeaderCards ImageCards(FitsHduLayout hdu) => throw new NotSupportedException(Refusal(hdu.Header));

    public IHduPixels Open(Stream file, FitsHduLayout hdu) => throw new NotSupportedException(Refusal(hdu.Header));
}
