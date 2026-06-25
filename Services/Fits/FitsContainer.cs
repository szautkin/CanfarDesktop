namespace CanfarDesktop.Services.Fits;

using System.Formats.Tar;
using System.IO.Compression;

/// <summary>
/// Opens a file as a FITS stream, transparently unwrapping the containers CADC commonly serves
/// around FITS products: a <b>tar</b> archive (the multi-product "download all" packaging — e.g.
/// an HST calibrated bundle whose members are <c>HST/product/xxx_flt.fits</c>) and/or <b>gzip</b>
/// compression (<c>.fits.gz</c>, <c>.tar.gz</c>). Returns a seekable stream positioned at the start
/// of a single FITS member, ready for <see cref="FitsParser"/>.
///
/// Without this, a tar bundle saved with a .fits-looking name is fed straight to the parser, which
/// reads the tar header as FITS cards, never finds an END card, and fails with the misleading
/// "FITS header exceeds maximum allowed size."
/// </summary>
public static class FitsContainer
{
    private const int TarBlock = 512;

    /// <summary>
    /// Open <paramref name="path"/> and return a seekable stream over a single FITS image,
    /// unwrapping a surrounding gzip and/or tar archive. The caller owns and disposes the returned
    /// stream. Throws <see cref="InvalidDataException"/> with an actionable message when the file is
    /// neither FITS nor a recognised FITS container.
    /// </summary>
    public static Stream OpenFits(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Unwrap(File.OpenRead(path), depth: 0);
    }

    /// <summary>
    /// Unwrap a single FITS member out of <paramref name="stream"/> (which it takes ownership of),
    /// peeling one container layer at a time. Disposes the input on every path except the plain-FITS
    /// pass-through, where the input <i>is</i> the result.
    /// </summary>
    public static Stream Unwrap(Stream stream, int depth)
    {
        var keep = false;
        try
        {
            if (depth > 3)
                throw new InvalidDataException("File is nested too deeply to open as FITS.");

            var magic = PeekMagic(stream);

            // Plain FITS — a primary header always begins with "SIMPLE  =".
            if (StartsWith(magic, "SIMPLE"u8))
            {
                keep = true;
                return stream;
            }

            // gzip — decompress fully into a seekable buffer, then re-detect the inner format
            // (handles both .fits.gz and .tar.gz).
            if (magic.Length >= 2 && magic[0] == 0x1F && magic[1] == 0x8B)
                return Unwrap(Decompress(stream), depth + 1);

            // tar — extract the first FITS member.
            if (LooksLikeTar(magic))
                return ExtractFirstFitsMember(stream);

            throw new InvalidDataException(
                $"This file is not a FITS image. It looks like {Describe(magic)} — " +
                "open the FITS file directly, or extract it first.");
        }
        finally
        {
            if (!keep) stream.Dispose();
        }
    }

    /// <summary>Read up to one tar block from the start, then rewind.</summary>
    private static byte[] PeekMagic(Stream stream)
    {
        stream.Position = 0;
        var buf = new byte[TarBlock];
        var n = ReadFull(stream, buf);
        stream.Position = 0;
        return n == buf.Length ? buf : buf[..n];
    }

    /// <summary>Decompress an entire gzip stream into a seekable, independent buffer.</summary>
    private static Stream Decompress(Stream gzip)
    {
        gzip.Position = 0;
        var ms = new MemoryStream();
        using (var gz = new GZipStream(gzip, CompressionMode.Decompress, leaveOpen: true))
            gz.CopyTo(ms);
        ms.Position = 0;
        return ms;
    }

    /// <summary>
    /// Find the first regular-file tar entry whose name ends in a FITS extension and copy it into a
    /// seekable buffer. Throws when the archive holds no FITS member.
    /// </summary>
    private static Stream ExtractFirstFitsMember(Stream tar)
    {
        tar.Position = 0;
        var members = 0;
        using var reader = new TarReader(tar, leaveOpen: true);
        while (reader.GetNextEntry() is { } entry)
        {
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                continue;
            members++;
            if (!IsFitsName(entry.Name) || entry.DataStream is not { } data)
                continue;

            var ms = new MemoryStream();
            data.CopyTo(ms);
            ms.Position = 0;
            return ms;
        }

        throw new InvalidDataException(members == 0
            ? "This tar archive is empty — there is no FITS file to open."
            : "This tar archive contains no .fits file to open.");
    }

    private static bool IsFitsName(string name)
    {
        var n = name.Trim();
        return n.EndsWith(".fits", StringComparison.OrdinalIgnoreCase)
            || n.EndsWith(".fit", StringComparison.OrdinalIgnoreCase)
            || n.EndsWith(".fts", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when the first block is a valid tar header, identified by its self-describing checksum
    /// (format-agnostic across v7/ustar/GNU/PAX). Plain FITS is ruled out before this is called.
    /// </summary>
    internal static bool LooksLikeTar(byte[] header)
    {
        if (header.Length < TarBlock) return false;

        var stored = ParseOctal(header.AsSpan(148, 8));
        if (stored < 0) return false;

        long unsigned = 0;
        long signed = 0;
        for (var i = 0; i < TarBlock; i++)
        {
            // The 8 checksum bytes are treated as spaces when the checksum is computed.
            var b = i is >= 148 and < 156 ? (byte)' ' : header[i];
            unsigned += b;
            signed += (sbyte)b;
        }
        return stored == unsigned || stored == signed;
    }

    /// <summary>Parse a leading run of octal digits (tar stores numbers as space/NUL-padded octal).</summary>
    private static long ParseOctal(ReadOnlySpan<byte> s)
    {
        long value = 0;
        var any = false;
        foreach (var b in s)
        {
            if (b is (byte)' ' or 0)
            {
                if (any) break;
                continue;
            }
            if (b is < (byte)'0' or > (byte)'7') return -1;
            value = (value << 3) + (b - '0');
            any = true;
        }
        return any ? value : -1;
    }

    private static string Describe(byte[] m)
    {
        if (m.Length > 0 && m[0] == (byte)'<') return "an HTML/XML document (perhaps a server error page)";
        if (m.Length >= 2 && m[0] == 0x1F && m[1] == 0x8B) return "a gzip archive";
        if (m.Length >= 2 && m[0] == (byte)'P' && m[1] == (byte)'K') return "a zip archive";
        return "an unrecognised or non-FITS file";
    }

    private static bool StartsWith(byte[] data, ReadOnlySpan<byte> prefix)
        => data.Length >= prefix.Length && data.AsSpan(0, prefix.Length).SequenceEqual(prefix);

    private static int ReadFull(Stream s, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = s.Read(buffer, total, buffer.Length - total);
            if (n == 0) break;
            total += n;
        }
        return total;
    }
}
