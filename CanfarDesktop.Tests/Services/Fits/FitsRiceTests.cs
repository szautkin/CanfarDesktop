using System.Text;
using CanfarDesktop.Models.Fits;
using CanfarDesktop.Services.Fits;
using Xunit;

namespace CanfarDesktop.Tests.Services.Fits;

public class FitsRiceTests
{
    // ── RiceDecode: hand-crafted bitstreams (cfitsio fits_rdecomp semantics) ──

    [Fact]
    public void ConstantTile_FsMinusOne_RepeatsSeed()
    {
        // seed=100 (BE int16), then fs nybble raw=0 → all deltas zero.
        var bytes = new byte[] { 0x00, 0x64, 0x00 };
        var pixels = FitsRice.RiceDecode(bytes, pixelCount: 5, blockSize: 32);
        Assert.Equal(new short[] { 100, 100, 100, 100, 100 }, pixels);
    }

    [Fact]
    public void UnaryOnlyBlock_FsZero_DecodesFoldedDeltas()
    {
        // seed=0; nybble raw=1 → fs=0; pixel bits: '1'(δ=0), '01'(δ=1→-1), '001'(δ=2→+1).
        // Bit stream: 0001 1010 01(pad) → 0x1A, 0x40.
        var bytes = new byte[] { 0x00, 0x00, 0x1A, 0x40 };
        var pixels = FitsRice.RiceDecode(bytes, pixelCount: 3, blockSize: 32);
        Assert.Equal(new short[] { 0, -1, 0 }, pixels);
    }

    [Fact]
    public void RemainderBlock_FsTwo_CombinesQuotientAndRemainder()
    {
        // seed=10; nybble raw=3 → fs=2. p0: '1'+'10' → δ=2→+1 → 11. p1: '01'+'11' → δ=7→-4 → 7.
        // Bits: 0011 1100 111(pad) → 0x3C, 0xE0.
        var bytes = new byte[] { 0x00, 0x0A, 0x3C, 0xE0 };
        var pixels = FitsRice.RiceDecode(bytes, pixelCount: 2, blockSize: 32);
        Assert.Equal(new short[] { 11, 7 }, pixels);
    }

    [Fact]
    public void HighEntropyBlock_FsMax_DecodesLiteral16BitDiffs()
    {
        // cfitsio escape: nybble raw=15 → fs=fsmax=14 → each pixel is a literal 16-bit folded
        // diff, NO unary quotient. seed=0; diffs 2 (→+1) and 3 (→−2).
        // Bits: 1111 | 0000000000000010 | 0000000000000011 | pad → F0 00 20 00 30.
        var bytes = new byte[] { 0x00, 0x00, 0xF0, 0x00, 0x20, 0x00, 0x30 };
        var pixels = FitsRice.RiceDecode(bytes, pixelCount: 2, blockSize: 32);
        Assert.Equal(new short[] { 1, -1 }, pixels);
    }

    [Fact]
    public void Parse_CompressedCube_FallsBackToFunpackAdvice()
    {
        // ZNAXIS=3: decoding only plane 1 and presenting it as the dataset would be silent data
        // loss — the parser must refuse and surface the funpack advice.
        var ms = new MemoryStream();
        WriteHeader(ms,
            Card("SIMPLE", "T"), Card("BITPIX", "16"), Card("NAXIS", "0"), Card("EXTEND", "T"));
        WriteHeader(ms,
            Card("XTENSION", "'BINTABLE'"), Card("BITPIX", "8"),
            Card("NAXIS", "2"), Card("NAXIS1", "8"), Card("NAXIS2", "1"),
            Card("PCOUNT", "3"), Card("GCOUNT", "1"), Card("TFIELDS", "1"),
            Card("TFORM1", "'1PB(3)  '"),
            Card("ZIMAGE", "T"), Card("ZCMPTYPE", "'RICE_1  '"), Card("ZBITPIX", "16"),
            Card("ZNAXIS", "3"), Card("ZNAXIS1", "3"), Card("ZNAXIS2", "1"), Card("ZNAXIS3", "5"));
        ms.Write(new byte[2880]);
        ms.Position = 0;

        var ex = Assert.Throws<InvalidDataException>(() => FitsParser.Parse(ms));
        Assert.Contains("funpack", ex.Message);
    }

    [Fact]
    public void Parse_QDescriptors_FallBackToFunpackAdvice()
    {
        // 64-bit 'Q' variable-length descriptors have a different row layout — misparsing them
        // would read garbage heap offsets, so the parser must refuse rather than decode.
        var ms = new MemoryStream();
        WriteHeader(ms,
            Card("SIMPLE", "T"), Card("BITPIX", "16"), Card("NAXIS", "0"), Card("EXTEND", "T"));
        WriteHeader(ms,
            Card("XTENSION", "'BINTABLE'"), Card("BITPIX", "8"),
            Card("NAXIS", "2"), Card("NAXIS1", "16"), Card("NAXIS2", "1"),
            Card("PCOUNT", "3"), Card("GCOUNT", "1"), Card("TFIELDS", "1"),
            Card("TFORM1", "'1QB(3)  '"),
            Card("ZIMAGE", "T"), Card("ZCMPTYPE", "'RICE_1  '"), Card("ZBITPIX", "16"),
            Card("ZNAXIS", "2"), Card("ZNAXIS1", "3"), Card("ZNAXIS2", "1"));
        ms.Write(new byte[2880]);
        ms.Position = 0;

        var ex = Assert.Throws<InvalidDataException>(() => FitsParser.Parse(ms));
        Assert.Contains("funpack", ex.Message);
    }

    [Fact]
    public void TruncatedStream_FillsRemainderWithPrev()
    {
        // Seed only, no block data: every pixel repeats the seed.
        var pixels = FitsRice.RiceDecode(new byte[] { 0x00, 0x2A }, pixelCount: 4, blockSize: 32);
        Assert.Equal(new short[] { 42, 42, 42, 42 }, pixels);
    }

    // ── End-to-end: synthetic fpack file through the full parser ─────────────

    [Fact]
    public void Parse_SyntheticFpackFile_DecompressesToExpectedPixels()
    {
        // 3×2 image as two 3×1 tiles: row 0 constant 7s, row 1 = [5,4,5] (unary-coded deltas).
        var tile0 = new byte[] { 0x00, 0x07, 0x00 };
        var tile1 = new byte[] { 0x00, 0x05, 0x1A, 0x40 };
        var heap = tile0.Concat(tile1).ToArray();

        var ms = new MemoryStream();
        WriteHeader(ms,
            Card("SIMPLE", "T"), Card("BITPIX", "16"), Card("NAXIS", "0"), Card("EXTEND", "T"));
        WriteHeader(ms,
            Card("XTENSION", "'BINTABLE'"), Card("BITPIX", "8"),
            Card("NAXIS", "2"), Card("NAXIS1", "8"), Card("NAXIS2", "2"),
            Card("PCOUNT", heap.Length.ToString()), Card("GCOUNT", "1"), Card("TFIELDS", "1"),
            Card("TTYPE1", "'COMPRESSED_DATA'"), Card("TFORM1", "'1PB(4)  '"),
            Card("ZIMAGE", "T"), Card("ZCMPTYPE", "'RICE_1  '"), Card("ZBITPIX", "16"),
            Card("ZNAXIS", "2"), Card("ZNAXIS1", "3"), Card("ZNAXIS2", "2"),
            Card("ZTILE1", "3"), Card("ZTILE2", "1"),
            Card("ZNAME1", "'BLOCKSIZE'"), Card("ZVAL1", "32"),
            Card("BSCALE", "1.0"), Card("BZERO", "0.0"));

        // Data area: 2 descriptor rows (nelem, offset — big-endian) then the heap, block-padded.
        var data = new MemoryStream();
        WriteBE(data, tile0.Length); WriteBE(data, 0);
        WriteBE(data, tile1.Length); WriteBE(data, tile0.Length);
        data.Write(heap);
        var padded = new byte[((data.Length + 2879) / 2880) * 2880];
        data.ToArray().CopyTo(padded, 0);
        ms.Write(padded);
        ms.Position = 0;

        var hdus = FitsParser.Parse(ms);
        var image = hdus.Single(h => h.ImageData is not null).ImageData!;
        Assert.Equal(3, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal(new float[] { 7, 7, 7, 5, 4, 5 }, image.Pixels);
        Assert.Equal(4, image.Min);
        Assert.Equal(7, image.Max);
    }

    /// <summary>Runs the real CFHT fpack file when present on this machine (skips elsewhere) —
    /// the sanity check that the decoder agrees with genuine fpack output at scale.</summary>
    [Fact]
    public void Parse_RealCfhtFpackFile_WhenAvailable()
    {
        const string path = @"C:\Users\szaut\OneDrive\Documents\1861468o.fits.fz.fits";
        if (!File.Exists(path)) return;

        using var stream = File.OpenRead(path);
        var hdus = FitsParser.Parse(stream);
        var image = hdus.Single(h => h.ImageData is not null).ImageData!;
        Assert.Equal(2148, image.Width);
        Assert.Equal(4128, image.Height);
        // A raw SITELLE frame: physical values must land in a sane unsigned-16 range (BZERO 32768).
        Assert.True(image.Min >= 0 && image.Max <= 65535, $"range {image.Min}..{image.Max}");
        Assert.True(image.Max > image.Min, "flat image would mean the decode collapsed");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static string Card(string key, string value) => $"{key,-8}= {value,-20}".PadRight(80)[..80];

    private static void WriteHeader(Stream s, params string[] cards)
    {
        var sb = new StringBuilder();
        foreach (var c in cards) sb.Append(c);
        sb.Append("END".PadRight(80));
        while (sb.Length % 2880 != 0) sb.Append(' ');
        var bytes = Encoding.ASCII.GetBytes(sb.ToString());
        s.Write(bytes);
    }

    private static void WriteBE(Stream s, int value)
    {
        s.WriteByte((byte)(value >> 24));
        s.WriteByte((byte)(value >> 16));
        s.WriteByte((byte)(value >> 8));
        s.WriteByte((byte)value);
    }
}
