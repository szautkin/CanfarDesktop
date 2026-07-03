using CanfarDesktop.Models.Fits;

namespace CanfarDesktop.Services.Fits;

/// <summary>
/// In-app fpack (RICE_1) decompression — a 1:1 port of the macOS FITSDecompressor/RiceDecoder
/// (itself matching cfitsio's fits_rdecomp for BYTEPIX=2). fpack stores the image as Rice-coded
/// tiles in a BINTABLE: each row's variable-length-array descriptor (big-endian nelem+offset)
/// points into the PCOUNT heap; tiles decode to int16 deltas seeded by a literal first pixel.
/// Scope matches macOS: ZCMPTYPE='RICE_1' with ZBITPIX=16 (the CFHT/MegaCam/SITELLE norm);
/// anything else throws NotSupportedException and the caller falls back to the funpack advice.
/// </summary>
public static class FitsRice
{
    private const long MaxPixels = 500_000_000;   // 500 Mpx cap (macOS FITSLimits parity)
    private const int MaxTileBytes = 64 * 1024 * 1024;

    public static bool CanDecompress(FitsHeader header)
        => header.GetBool("ZIMAGE")
           && string.Equals(header.GetString("ZCMPTYPE")?.Trim(), "RICE_1", StringComparison.Ordinal)
           && header.GetInt("ZBITPIX") == 16;

    /// <summary>
    /// Decompress a RICE_1 HDU. <paramref name="tableAndHeap"/> is the HDU's full data area
    /// (main table rows immediately followed by the heap), as read from the file.
    /// </summary>
    public static FitsImageData Decompress(FitsHeader header, byte[] tableAndHeap)
    {
        if (!CanDecompress(header))
            throw new NotSupportedException(
                $"Unsupported FITS tile compression (ZCMPTYPE='{header.GetString("ZCMPTYPE")}', ZBITPIX={header.GetInt("ZBITPIX")}).");

        var imageWidth = header.GetInt("ZNAXIS1");
        var imageHeight = header.GetInt("ZNAXIS2");
        if (imageWidth <= 0 || imageHeight <= 0)
            throw new InvalidDataException($"Compressed FITS: bad image dimensions {imageWidth}×{imageHeight}.");
        var totalPixels = (long)imageWidth * imageHeight;
        if (totalPixels > MaxPixels)
            throw new NotSupportedException($"Compressed FITS: image too large ({totalPixels} px, cap {MaxPixels}).");

        var tileWidth = Math.Max(1, header.GetInt("ZTILE1", imageWidth));
        var tileHeight = Math.Max(1, header.GetInt("ZTILE2", 1));
        var blockSize = Math.Max(1, header.GetInt("ZVAL1", 32));

        var tableRowBytes = header.NAxis1;     // BINTABLE row width in bytes
        var tableRows = header.NAxis2;         // rows = tiles
        var pcount = header.GetInt("PCOUNT");
        long heapStart = (long)tableRowBytes * tableRows;
        if (tableRowBytes < 8 || tableRows <= 0 || pcount < 0 || heapStart + pcount > tableAndHeap.Length)
            throw new InvalidDataException("Compressed FITS: table/heap geometry does not fit the data area.");

        var nTilesX = (imageWidth + tileWidth - 1) / tileWidth;
        var nTilesY = (imageHeight + tileHeight - 1) / tileHeight;
        var tilesToDecode = Math.Min(nTilesX * nTilesY, tableRows);

        var raw = new int[totalPixels];
        for (var tileIdx = 0; tileIdx < tilesToDecode; tileIdx++)
        {
            var rowStart = (long)tileIdx * tableRowBytes;
            var nelem = ReadBigEndianInt32(tableAndHeap, rowStart);
            var offset = ReadBigEndianInt32(tableAndHeap, rowStart + 4);
            if (nelem < 0 || offset < 0)
                throw new InvalidDataException($"Compressed FITS: malformed descriptor in row {tileIdx}.");
            if (nelem > MaxTileBytes)
                throw new InvalidDataException($"Compressed FITS: tile {tileIdx} exceeds the {MaxTileBytes / (1024 * 1024)} MB cap.");
            var tileStart = heapStart + offset;
            if (nelem > pcount || tileStart + nelem > heapStart + pcount)
                throw new InvalidDataException($"Compressed FITS: tile {tileIdx} extends beyond the heap.");

            var tileCol = tileIdx % nTilesX;
            var tileRow = tileIdx / nTilesX;
            var tilePxWidth = Math.Min(tileWidth, imageWidth - tileCol * tileWidth);
            var tilePxHeight = Math.Min(tileHeight, imageHeight - tileRow * tileHeight);
            if (tilePxWidth <= 0 || tilePxHeight <= 0) continue;

            var decoded = RiceDecode(
                tableAndHeap.AsSpan((int)tileStart, nelem), tilePxWidth * tilePxHeight, blockSize);

            var destRowStart = tileRow * tileHeight;
            for (var py = 0; py < tilePxHeight; py++)
            {
                var srcBase = py * tilePxWidth;
                var destBase = (long)(destRowStart + py) * imageWidth + tileCol * tileWidth;
                for (var px = 0; px < tilePxWidth; px++)
                {
                    var dest = destBase + px;
                    if (dest < raw.Length) raw[dest] = decoded[srcBase + px];
                }
            }
        }

        // Physical values: BSCALE/BZERO from the bintable header apply to the ORIGINAL integers
        // (BZERO=32768 is the standard unsigned-uint16-as-int16 convention).
        var bscale = (float)header.BScale;
        var bzero = (float)header.BZero;
        var pixels = new float[totalPixels];
        float min = float.MaxValue, max = float.MinValue;
        for (var i = 0; i < pixels.Length; i++)
        {
            var v = bzero + bscale * raw[i];
            pixels[i] = v;
            if (v < min) min = v;
            if (v > max) max = v;
        }

        return new FitsImageData
        {
            Pixels = pixels,
            Width = imageWidth,
            Height = imageHeight,
            Min = min,
            Max = max,
            Wcs = WcsInfo.FromHeader(header),
            Unit = header.GetString("BUNIT"),
        };
    }

    // ── RICE_1 decoder (cfitsio fits_rdecomp, BYTEPIX=2) ─────────────────────

    /// <summary>Decode one Rice-coded tile to signed 16-bit pixels (as ints).</summary>
    internal static short[] RiceDecode(ReadOnlySpan<byte> bytes, int pixelCount, int blockSize)
    {
        var output = new short[pixelCount];
        if (pixelCount == 0) return output;

        var reader = new BitReader(bytes.ToArray());

        // Literal first pixel: big-endian int16 seed (the first block iteration emits pixel 0).
        var high = reader.ReadByte();
        var low = reader.ReadByte();
        if (high < 0 || low < 0) throw new InvalidDataException("Rice tile: truncated seed pixel.");
        int prev = unchecked((short)((high << 8) | low));

        var produced = 0;
        while (produced < pixelCount)
        {
            var blockCount = Math.Min(blockSize, pixelCount - produced);

            // fs nybble, then fs = raw - 1: raw 0 → all-zero deltas; raw 1 → unary-only.
            var fsRaw = reader.ReadBits(4);
            if (fsRaw < 0)
            {
                for (; produced < pixelCount; produced++) output[produced] = unchecked((short)prev);
                break;
            }
            var fs = fsRaw - 1;

            if (fs < 0)
            {
                for (var i = 0; i < blockCount; i++) output[produced++] = unchecked((short)prev);
                continue;
            }

            var fsMask = (1 << fs) - 1;
            for (var i = 0; i < blockCount; i++)
            {
                var q = 0;
                int bit;
                while ((bit = reader.ReadBit()) == 0) q++;
                if (bit < 0)
                {
                    // Stream exhausted mid-block: remaining pixels repeat prev (delta 0).
                    output[produced++] = unchecked((short)prev);
                    continue;
                }
                var r = 0;
                for (var b = 0; b < fs; b++)
                {
                    var rb = reader.ReadBit();
                    if (rb < 0) break; // partial remainder — treat missing bits as zero
                    r = (r << 1) | rb;
                }
                var delta = (q << fs) | (r & fsMask);
                var signedDelta = (delta & 1) == 0 ? delta >> 1 : -((delta + 1) >> 1); // unfold
                prev = unchecked((short)(prev + signedDelta));
                output[produced++] = unchecked((short)prev);
            }
        }

        return output;
    }

    private static int ReadBigEndianInt32(byte[] data, long offset)
    {
        if (offset < 0 || offset + 4 > data.Length) return -1;
        return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
    }

    /// <summary>MSB-first continuous bit stream (matches cfitsio). Reads return -1 when exhausted.</summary>
    private sealed class BitReader
    {
        private readonly byte[] _bytes;
        private int _bitPos;
        private readonly int _totalBits;

        public BitReader(byte[] bytes)
        {
            _bytes = bytes;
            _totalBits = bytes.Length * 8;
        }

        public int ReadBit()
        {
            if (_bitPos >= _totalBits) return -1;
            var bit = (_bytes[_bitPos >> 3] >> (7 - (_bitPos & 7))) & 1;
            _bitPos++;
            return bit;
        }

        public int ReadBits(int n)
        {
            if (_bitPos + n > _totalBits) return -1;
            var result = 0;
            for (var i = 0; i < n; i++) result = (result << 1) | ReadBit();
            return result;
        }

        /// <summary>Up to 8 bits, zero-padded low when fewer remain; -1 only when fully exhausted.</summary>
        public int ReadByte()
        {
            if (_bitPos >= _totalBits) return -1;
            var available = Math.Min(8, _totalBits - _bitPos);
            var result = 0;
            for (var i = 0; i < available; i++) result = (result << 1) | ReadBit();
            return result << (8 - available);
        }
    }
}
