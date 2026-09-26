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
    /// <summary>A stream that cannot say where it is reports nothing rather than throwing mid-parse.</summary>
    private static long SafePosition(Stream stream)
    {
        try { return stream.CanSeek ? stream.Position : 0; }
        catch { return 0; }
    }

    private static long SafeLength(Stream stream)
    {
        try { return stream.CanSeek ? stream.Length : 0; }
        catch { return 0; }
    }

    /// <summary>A FITS file is read and written in blocks of this many bytes.</summary>
    public const int BlockSize = 2880;

    /// <summary>A header card is this many bytes.</summary>
    public const int CardSize = 80;

    /// <summary>How much raw image data is read at a time: 4 MB, a whole number of pixels at any BITPIX.</summary>
    private const int ChunkBytes = 4 * 1024 * 1024;

    /// <summary>
    /// Parse all HDUs from a FITS file stream. Only reads image data for HDUs with NAXIS >= 2.
    /// </summary>
    /// <param name="progress">
    /// Told after each extension, so a caller can show something moving. A mosaic frame has forty-one
    /// of them and each is decompressed in turn; without this the whole read is one opaque call and
    /// the viewer can only spin.
    /// </param>
    /// <param name="availableMemory">Free memory to judge a large image against; null asks the
    /// machine (<see cref="FitsMemoryBudget"/>). Given by tests, so the answer does not depend on them.</param>
    public static List<FitsHdu> Parse(
        Stream stream, IProgress<Helpers.FitsParseProgress>? progress = null, long? availableMemory = null)
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
            var dataBytes = DataSize(header);

            if (header.GetBool("ZIMAGE"))
            {
                // fpack/tile-compressed image: the pixels live as Rice-coded tiles in a BINTABLE
                // heap. RICE_1/16-bit (the CFHT norm) decompresses in-app (FitsRice, macOS parity);
                // other variants are recorded + skipped, surfacing the funpack advice after the
                // scan if no readable image HDU exists.
                const long maxCompressedBytes = 512L * 1024 * 1024;
                if (FitsRice.CanDecompress(header) && dataBytes > 0 && dataBytes <= maxCompressedBytes)
                {
                    var buffer = new byte[AlignToBlock(dataBytes)];
                    var read = ReadFully(stream, buffer, buffer.Length);
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
                var dataStart = stream.Position;
                // The 2D viewer reads only the FIRST plane (e.g. plane 1 of a WFPC2 c0f 4-chip cube);
                // the cube viewer handles the rest. But the parser must still advance past ALL planes
                // so the next HDU starts at the right offset — otherwise planes 2..N are misread as a
                // header and blow the max-header-size guard.
                imageData = ReadImageData(stream, header, availableMemory);
                hasReadableImage = true;
                var hduDataBytes = AlignToBlock(DataSize(header));
                var consumed = stream.Position - dataStart;
                if (hduDataBytes > consumed) SkipBytes(stream, hduDataBytes - consumed);
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

            // After the extension rather than before it: reporting the one about to be read would
            // name a thing the file might not have, and the bytes would not have moved yet.
            progress?.Report(new Helpers.FitsParseProgress(
                HdusParsed: hdus.Count,
                BytesRead: SafePosition(stream),
                TotalBytes: SafeLength(stream),
                CurrentHdu: header.GetString("EXTNAME")));
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

            var dataBytes = DataSize(header);
            if (dataBytes > 0)
                SkipBytes(stream, AlignToBlock(dataBytes));
        }
        return headers;
    }

    /// <summary>
    /// Read a single FITS header from the current stream position.
    /// Returns null if the stream is at EOF or the block is not valid FITS.
    /// </summary>
    /// <param name="rawCards">
    /// When given, receives every card before END exactly as the file has it, 80 characters each —
    /// what a writer copies so that a card it does not change comes out byte for byte as it went in.
    /// Blank padding cards are left out.
    /// </param>
    public static FitsHeader? ReadHeader(Stream stream, List<string>? rawCards = null)
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
            var bytesRead = ReadFully(stream, buffer, BlockSize);
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

                if (rawCards is not null)
                {
                    var raw = System.Text.Encoding.ASCII.GetString(buffer, i, CardSize);
                    if (!string.IsNullOrWhiteSpace(raw)) rawCards.Add(raw);
                }
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
    /// <param name="availableMemory">See <see cref="Parse"/>.</param>
    public static FitsImageData ReadImageData(Stream stream, FitsHeader header, long? availableMemory = null)
    {
        var width = header.NAxis1;
        var height = header.NAxis2;
        var bitpix = header.BitPix;
        var bscale = header.BScale;
        var bzero = header.BZero;
        var pixelCount = (long)width * height;
        if (pixelCount > int.MaxValue)
            throw new NotSupportedException($"Image too large: {width}x{height} ({pixelCount} pixels)");
        if (bitpix is not (8 or 16 or 32 or -32 or -64))
            throw new NotSupportedException($"Unsupported BITPIX: {bitpix}");

        // Judged on what the pixels take in MEMORY — four bytes each, whatever the file stores — and
        // before anything is allocated. Under the floor there is nothing to ask the machine.
        var pixelBytes = pixelCount * sizeof(float);
        if (pixelBytes > FitsMemoryBudget.Floor
            && FitsMemoryBudget.Refusal(width, height, pixelBytes,
                availableMemory ?? FitsMemoryBudget.AvailableBytes()) is { } refusal)
            throw new NotSupportedException(refusal);

        var bytesPerPixel = Math.Abs(bitpix) / 8;
        var dataSize = pixelCount * bytesPerPixel;
        var pixels = new float[(int)pixelCount];
        var min = float.MaxValue;
        var max = float.MinValue;

        // Converted as it is read, a chunk at a time. Reading the raw bytes whole first doubled what a
        // load needed — 3.3 GB at its peak for a 1.6 GB tile — for bytes that are dead as soon as they
        // are converted. The block padding after the data is left for the caller, which skips to the
        // next HDU by position.
        var chunk = new byte[ChunkBytes];
        var chunkPixels = ChunkBytes / bytesPerPixel;
        for (long done = 0; done < pixelCount;)
        {
            var count = (int)Math.Min(chunkPixels, pixelCount - done);
            var wanted = count * bytesPerPixel;
            var got = ReadFully(stream, chunk, wanted);
            if (got < wanted)
                throw new InvalidDataException(
                    $"FITS data truncated: expected {dataSize} bytes, got {done * bytesPerPixel + got}");

            for (var i = 0; i < count; i++)
            {
                var offset = i * bytesPerPixel;
                float raw = bitpix switch
                {
                    8 => chunk[offset],
                    16 => BinaryPrimitives.ReadInt16BigEndian(chunk.AsSpan(offset)),
                    32 => BinaryPrimitives.ReadInt32BigEndian(chunk.AsSpan(offset)),
                    -32 => BinaryPrimitives.ReadSingleBigEndian(chunk.AsSpan(offset)),
                    _ => (float)BinaryPrimitives.ReadDoubleBigEndian(chunk.AsSpan(offset)),
                };

                var physical = (float)(bzero + bscale * raw);

                // Exclude NaN/Inf from min/max
                if (float.IsFinite(physical))
                {
                    if (physical < min) min = physical;
                    if (physical > max) max = physical;
                }

                pixels[done + i] = physical;
            }

            done += count;
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

    /// <summary>
    /// How many bytes of data follow this header, before the padding to a whole block — the FITS rule,
    /// PCOUNT's heap included.
    /// </summary>
    public static long DataSize(FitsHeader header)
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

    /// <summary>A size rounded up to whole blocks, as FITS stores every header and data unit.</summary>
    public static long AlignToBlock(long size) =>
        size <= 0 ? 0 : ((size + BlockSize - 1) / BlockSize) * BlockSize;

    /// <summary>
    /// Read <paramref name="count"/> bytes, or as many as there are before the end. One Read is not
    /// that: a stream may return fewer bytes than asked while more are coming, and a decompressing
    /// one routinely does — which the image path took for a truncated file.
    /// </summary>
    private static int ReadFully(Stream stream, byte[] buffer, int count)
    {
        var read = 0;
        while (read < count)
        {
            var n = stream.Read(buffer, read, count - read);
            if (n == 0) break;
            read += n;
        }
        return read;
    }

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
