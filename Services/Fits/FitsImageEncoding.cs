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
/// An image tile-compressed into a binary table, as fpack writes it (the ZIMAGE convention). Read the way
/// the viewer reads it — RICE_1 at 16 bits, a plain image (the CFHT norm: MegaPrime's raw frames) — but a
/// tile at a time, and only the tiles a cut touches: a 36-CCD frame is not decompressed to cut one CCD's
/// corner. Any other kind is recognised, and refused with the reason and what to do about it.
/// </summary>
public sealed partial class TileCompressedImage : IFitsImageEncoding
{
    public bool Recognises(FitsHeader header) => header.IsTileCompressed;

    public string? Refusal(FitsHeader header)
    {
        if (FitsRice.CanDecompress(header)) return null;
        var kind = $"{header.GetString("ZCMPTYPE") ?? "?"}, {header.GetInt("ZBITPIX")}-bit, {header.GetInt("ZNAXIS")} axes";
        return string.Format(Cutouts.CutoutRules.T("Cutout_LocalCompressed",
            "The downloaded file is tile-compressed in a way the app cannot read ({0}); funpack makes a plain .fits of it that can be cut."),
            kind);
    }

    /// <summary>
    /// The header the image had before it was compressed — cfitsio's rule: the Z keywords give back
    /// BITPIX, NAXIS and NAXISn; the table's own keywords and the compression's go; everything else,
    /// WCS, BSCALE and BZERO among it, is the image's and stays as it is.
    /// </summary>
    public FitsHeaderCards ImageCards(FitsHduLayout hdu)
    {
        var h = hdu.Header;
        var cards = new FitsHeaderCards([]);
        cards.SetString("XTENSION", "IMAGE", "image extension");
        cards.Set("BITPIX", (long)h.GetInt("ZBITPIX"));
        cards.Set("NAXIS", (long)h.GetInt("ZNAXIS"));
        for (var n = 1; n <= h.GetInt("ZNAXIS"); n++) cards.Set($"NAXIS{n}", (long)h.GetInt($"ZNAXIS{n}"));
        cards.Set("PCOUNT", 0L);
        cards.Set("GCOUNT", 1L);
        foreach (var card in hdu.RawCards)
        {
            var keyword = FitsHeaderCards.KeywordOf(card);
            if (keyword == "ZBLANK") cards.Set("BLANK", (long)h.GetInt("ZBLANK"));
            else if (!TableOrCompression().IsMatch(keyword)) cards.Add(card);
        }
        return cards;
    }

    public IHduPixels Open(Stream file, FitsHduLayout hdu) => new Tiles(file, hdu);

    /// <summary>
    /// The rows of the image, decoded from the tiles that hold them — each tile read once from where it
    /// lies, and kept only while the rows being read are in it.
    /// </summary>
    private sealed class Tiles(Stream file, FitsHduLayout hdu) : IHduPixels
    {
        private readonly FitsRice.RiceTiles _tiles = FitsRice.RiceTiles.From(hdu.Header);
        private readonly long _heapBytes = hdu.Header.GetInt("PCOUNT");
        private readonly Dictionary<int, short[]> _decoded = [];
        private int _tileRow = -1;

        public int BytesPerPixel => 2;

        public void ReadRow(long row, long x0, Span<byte> destination)
        {
            var count = destination.Length / 2;
            if (row / _tiles.TileHeight != _tileRow)
            {
                _decoded.Clear(); // a new row of tiles: the last one's are done with
                _tileRow = (int)(row / _tiles.TileHeight);
            }

            for (var i = 0; i < count; i++)
            {
                var x = x0 + i;
                var tile = _tiles.TileAt(x, row);
                if (!_decoded.TryGetValue(tile, out var pixels)) _decoded[tile] = pixels = Decode(tile);

                var width = _tiles.SizeOf(tile).Width;
                var inTile = (row - (long)(tile / _tiles.Across) * _tiles.TileHeight) * width + (x - (long)(tile % _tiles.Across) * _tiles.TileWidth);
                System.Buffers.Binary.BinaryPrimitives.WriteInt16BigEndian(destination[(2 * i)..], pixels[inTile]);
            }
        }

        private short[] Decode(int tile)
        {
            Span<byte> descriptor = stackalloc byte[8];
            file.Position = hdu.DataStart + (long)tile * _tiles.RowBytes;
            file.ReadExactly(descriptor);
            var (length, offset) = _tiles.Checked(tile,
                System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(descriptor),
                System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(descriptor[4..]), _heapBytes);

            var bytes = new byte[length];
            file.Position = hdu.DataStart + _tiles.HeapOffset + offset;
            file.ReadExactly(bytes);
            var (width, height) = _tiles.SizeOf(tile);
            return FitsRice.RiceDecode(bytes, 0, length, width * height, _tiles.BlockSize);
        }

        public void Dispose() => _decoded.Clear(); // the stream is the caller's
    }

    [System.Text.RegularExpressions.GeneratedRegex(
        @"^(XTENSION|BITPIX|NAXIS\d*|PCOUNT|GCOUNT|THEAP|TFIELDS|TTYPE\d+|TFORM\d+|TUNIT\d+|TSCAL\d+|TZERO\d+|TNULL\d+|TDIM\d+|TDISP\d+|" +
        @"ZIMAGE|ZCMPTYPE|ZBITPIX|ZNAXIS\d*|ZTILE\d+|ZNAME\d+|ZVAL\d+|ZQUANTIZ|ZDITHER0|ZSIMPLE|ZTENSION|ZEXTEND|ZBLOCKED|" +
        @"ZPCOUNT|ZGCOUNT|ZHECKSUM|ZDATASUM|CHECKSUM|DATASUM)$")]
    private static partial System.Text.RegularExpressions.Regex TableOrCompression();
}
