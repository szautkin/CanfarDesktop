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

        while (stream.Position < stream.Length)
        {
            var header = ReadHeader(stream);
            if (header is null) break;

            FitsImageData? imageData = null;
            var dataBytes = CalculateDataSize(header);

            if (header.NAxis >= 2 && header.NAxis1 > 0 && header.NAxis2 > 0)
            {
                imageData = ReadImageData(stream, header);
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
        };
    }

    private static long CalculateDataSize(FitsHeader header)
    {
        var naxis = header.NAxis;
        if (naxis is 0 or > 999) return 0; // FITS spec: NAXIS ≤ 999

        long size = Math.Abs(header.BitPix) / 8;
        for (var i = 1; i <= naxis; i++)
            size *= header.GetInt($"NAXIS{i}");

        return size;
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
