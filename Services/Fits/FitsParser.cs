namespace CanfarDesktop.Services.Fits;

using System.Buffers.Binary;
using CanfarDesktop.Models.Fits;

/// <summary>
/// Pure static FITS file parser. Reads headers and image data from standard FITS files.
/// Handles BITPIX 8/16/32/-32/-64 with BSCALE/BZERO physical value conversion.
/// All I/O is synchronous on the provided stream — caller should wrap in Task.Run.
/// </summary>
public static class FitsParser
{
    private const int BlockSize = 2880;
    private const int CardSize = 80;

    /// <summary>
    /// Parse all HDUs from a FITS file stream. Only reads image data for HDUs with NAXIS >= 2.
    /// </summary>
    public static List<FitsHdu> Parse(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var hdus = new List<FitsHdu>();
        var index = 0;
        var sawCompressedImage = false;
        string? compressionType = null;
        var hasReadableImage = false;

        while (stream.Position < stream.Length)
        {
            var header = ReadHeader(stream);
            if (header is null) break;

            FitsImageData? imageData = null;
            var dataBytes = CalculateDataSize(header);

            if (header.GetBool("ZIMAGE"))
            {
                // fpack/tile-compressed image: the pixels live as Rice-coded tiles in a BINTABLE
                // heap. RICE_1/16-bit (the CFHT norm) decompresses in-app (FitsRice, macOS parity);
                // other variants are recorded + skipped, surfacing the funpack advice after the
                // scan if no readable image HDU exists.
                const long maxCompressedBytes = 512L * 1024 * 1024;
                if (FitsRice.CanDecompress(header) && dataBytes > 0 && dataBytes <= maxCompressedBytes)
                {
                    var aligned = AlignToBlock(dataBytes);
                    var buffer = new byte[aligned];
                    var read = 0;
                    while (read < aligned)
                    {
                        var n = stream.Read(buffer, read, (int)(aligned - read));
                        if (n == 0) break;
                        read += n;
                    }
                    if (read < dataBytes)
                    {
                        // Truncated compressed HDU: degrade like the old skip path (EOF-tolerant)
                        // so readable plain HDUs elsewhere in the file still open.
                        sawCompressedImage = true;
                        compressionType ??= header.GetString("ZCMPTYPE");
                    }
                    else
                    {
                        try
                        {
                            imageData = FitsRice.Decompress(header, buffer);
                            hasReadableImage = true;
                        }
                        catch (Exception ex) when (ex is not OutOfMemoryException)
                        {
                            // Corrupt tiles / unexpected geometry: fall back to the funpack advice.
                            System.Diagnostics.Debug.WriteLine($"fpack decompression failed: {ex.Message}");
                            sawCompressedImage = true;
                            compressionType ??= header.GetString("ZCMPTYPE");
                        }
                    }
                }
                else
                {
                    sawCompressedImage = true;
                    compressionType ??= header.GetString("ZCMPTYPE");
                    if (dataBytes > 0)
                        SkipBytes(stream, AlignToBlock(dataBytes));
                }
            }
            else if (header.NAxis >= 2 && header.NAxis1 > 0 && header.NAxis2 > 0)
            {
                imageData = ReadImageData(stream, header);
                hasReadableImage = true;
            }
            else if (dataBytes > 0)
            {
                // Skip non-image data (binary tables, etc.)
                SkipBytes(stream, AlignToBlock(dataBytes));
            }

            hdus.Add(new FitsHdu
            {
                Header = header,
                ImageData = imageData,
                Index = index++,
            });
        }

        if (sawCompressedImage && !hasReadableImage)
        {
            var algo = string.IsNullOrEmpty(compressionType) ? "tile/Rice" : compressionType;
            throw new InvalidDataException(
                $"This FITS file is fpack-compressed (ZCMPTYPE='{algo}') and cannot be opened directly. " +
                "Run 'funpack' to decompress it into a plain .fits, then open that file.");
        }

        return hdus;
    }

    /// <summary>
    /// Parse only the headers (no image data) — fast metadata scan.
    /// </summary>
    public static List<FitsHeader> ParseHeaders(Stream stream)
    {
        var headers = new List<FitsHeader>();
        while (stream.Position < stream.Length)
        {
            var header = ReadHeader(stream);
            if (header is null) break;
            headers.Add(header);

            var dataBytes = CalculateDataSize(header);
            if (dataBytes > 0)
                SkipBytes(stream, AlignToBlock(dataBytes));
        }
        return headers;
    }

    /// <summary>
    /// Read a single FITS header from the current stream position.
    /// Returns null if the stream is at EOF or the block is not valid FITS.
    /// </summary>
    public static FitsHeader? ReadHeader(Stream stream)
    {
        var header = new FitsHeader();
        const int maxHeaderBlocks = 1000; // ~2.8 MB max header size
        var buffer = new byte[BlockSize];
        var foundEnd = false;
        var blockCount = 0;

        while (!foundEnd)
        {
            if (++blockCount > maxHeaderBlocks)
                throw new InvalidDataException("FITS header exceeds maximum allowed size.");
            var bytesRead = stream.Read(buffer, 0, BlockSize);
            if (bytesRead < BlockSize) return header.Cards.Count > 0 ? header : null;

            for (var i = 0; i < BlockSize; i += CardSize)
            {
                var card = ParseCard(buffer.AsSpan(i, CardSize));

                if (card.Keyword == "END")
                {
                    foundEnd = true;
                    break;
                }

                if (!string.IsNullOrWhiteSpace(card.Keyword))
                    header.Add(card);
            }
        }

        return header;
    }

    /// <summary>
    /// Parse a single 80-character FITS card.
    /// </summary>
    internal static FitsCard ParseCard(ReadOnlySpan<byte> bytes)
    {
        var line = System.Text.Encoding.ASCII.GetString(bytes);

        var keyword = line[..8].Trim();
        if (keyword == "END") return new FitsCard("END", "", "");

        if (line.Length < 10 || line[8] != '=' || line[9] != ' ')
            return new FitsCard(keyword, "", line.Length > 8 ? line[8..].Trim() : "");

        var valueComment = line[10..];
        var value = "";
        var comment = "";

        // Check for string value (enclosed in single quotes)
        if (valueComment.TrimStart().StartsWith('\''))
        {
            var start = valueComment.IndexOf('\'') + 1;
            var end = valueComment.IndexOf('\'', start);
            if (end > start)
            {
                value = valueComment[start..end];
                var slashIdx = valueComment.IndexOf('/', end);
                if (slashIdx >= 0) comment = valueComment[(slashIdx + 1)..].Trim();
            }
        }
        else
        {
            var slashIdx = valueComment.IndexOf('/');
            if (slashIdx >= 0)
            {
                value = valueComment[..slashIdx].Trim();
                comment = valueComment[(slashIdx + 1)..].Trim();
            }
            else
            {
                value = valueComment.Trim();
            }
        }

        return new FitsCard(keyword, value, comment);
    }

    /// <summary>
    /// Read image data from the stream based on the header's BITPIX, NAXIS1, NAXIS2.
    /// Applies BSCALE/BZERO to produce physical float values.
    /// </summary>
    public static FitsImageData ReadImageData(Stream stream, FitsHeader header)
    {
        var width = header.NAxis1;
        var height = header.NAxis2;
        var bitpix = header.BitPix;
        var bscale = header.BScale;
        var bzero = header.BZero;
        var pixelCount = (long)width * height;
        if (pixelCount > int.MaxValue)
            throw new NotSupportedException($"Image too large: {width}x{height} ({pixelCount} pixels)");

        var bytesPerPixel = Math.Abs(bitpix) / 8;
        var dataSize = pixelCount * bytesPerPixel;
        const long maxDataBytes = 512L * 1024 * 1024; // 512 MB cap
        if (dataSize > maxDataBytes)
            throw new NotSupportedException($"Image data too large ({dataSize / (1024 * 1024)} MB, max {maxDataBytes / (1024 * 1024)} MB)");
        var alignedSize = (int)AlignToBlock(dataSize);

        var rawBuffer = new byte[alignedSize];
        var bytesRead = stream.Read(rawBuffer, 0, alignedSize);
        if (bytesRead < dataSize)
            throw new InvalidDataException($"FITS data truncated: expected {dataSize} bytes, got {bytesRead}");

        var pixels = new float[(int)pixelCount];
        var min = float.MaxValue;
        var max = float.MinValue;

        for (var i = 0; i < pixelCount; i++)
        {
            var offset = i * bytesPerPixel;
            float raw = bitpix switch
            {
                8 => rawBuffer[offset],
                16 => BinaryPrimitives.ReadInt16BigEndian(rawBuffer.AsSpan(offset)),
                32 => BinaryPrimitives.ReadInt32BigEndian(rawBuffer.AsSpan(offset)),
                -32 => BinaryPrimitives.ReadSingleBigEndian(rawBuffer.AsSpan(offset)),
                -64 => (float)BinaryPrimitives.ReadDoubleBigEndian(rawBuffer.AsSpan(offset)),
                _ => throw new NotSupportedException($"Unsupported BITPIX: {bitpix}")
            };

            var physical = (float)(bzero + bscale * raw);

            // Exclude NaN/Inf from min/max
            if (float.IsFinite(physical))
            {
                if (physical < min) min = physical;
                if (physical > max) max = physical;
            }

            pixels[i] = physical;
        }

        if (min == float.MaxValue) { min = 0; max = 1; } // all NaN edge case

        return new FitsImageData
        {
            Pixels = pixels,
            Width = width,
            Height = height,
            Min = min,
            Max = max,
            Wcs = WcsInfo.FromHeader(header),
            Unit = header.GetString("BUNIT")?.Trim(),
        };
    }

    private static long CalculateDataSize(FitsHeader header)
    {
        var naxis = header.NAxis;
        if (naxis is 0 or > 999) return 0; // FITS spec: NAXIS ≤ 999

        long axes = 1;
        for (var i = 1; i <= naxis; i++)
            axes *= header.GetInt($"NAXIS{i}");

        // FITS §4.4.1: size = |BITPIX|/8 × GCOUNT × (PCOUNT + NAXIS1×…×NAXISn).
        // PCOUNT is the variable-length heap — fpack BINTABLEs put the whole compressed image
        // there (megabytes), and omitting it made the skip land mid-heap, so the next "header"
        // was compressed garbage that ran into the max-header-size guard.
        long pcount = header.GetInt("PCOUNT");
        long gcount = Math.Max(1, header.GetInt("GCOUNT"));
        return Math.Abs(header.BitPix) / 8 * gcount * (pcount + axes);
    }

    private static long AlignToBlock(long size) =>
        size <= 0 ? 0 : ((size + BlockSize - 1) / BlockSize) * BlockSize;

    private static void SkipBytes(Stream stream, long count)
    {
        if (stream.CanSeek)
            stream.Seek(count, SeekOrigin.Current);
        else
        {
            var buffer = new byte[Math.Min(count, 8192)];
            var remaining = count;
            while (remaining > 0)
            {
                var toRead = (int)Math.Min(remaining, buffer.Length);
                var read = stream.Read(buffer, 0, toRead);
                if (read == 0) break;
                remaining -= read;
            }
        }
    }
}
