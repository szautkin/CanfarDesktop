using System.Text;

namespace CanfarDesktop.Helpers;

/// <summary>
/// Cheap FITS shape sniff: is this file a spectral cube (3+ axes with a real third dimension) or a
/// 2D image? Reads only header blocks — no pixel data — so it is safe to run right after a download
/// to pick the viewer to suggest (Cube Viewer vs FITS Viewer). Follows a data-less primary header
/// (NAXIS=0, the common MEF layout) to the first extension header. Any parse trouble returns false:
/// the 2D FITS viewer is the safe default suggestion.
/// </summary>
public enum FitsKind { NotFits, Image2D, Cube }

public static class FitsSniff
{
    private const int BlockSize = 2880;
    private const int CardSize = 80;
    private const int MaxHeaderBlocks = 64; // defensive cap per HDU header

    /// <summary>
    /// Content-based classification (extension lies: a mis-served download can put FITS bytes in a
    /// ".png"). Magic check first — every FITS starts with the literal card "SIMPLE  =" — then the
    /// cube-vs-2D shape sniff.
    /// </summary>
    public static FitsKind ClassifyFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var magic = new byte[9];
            if (stream.Read(magic, 0, 9) < 9 ||
                !System.Text.Encoding.ASCII.GetString(magic).StartsWith("SIMPLE  =", StringComparison.Ordinal))
                return FitsKind.NotFits;
            stream.Position = 0;
            return IsLikelyCube(stream) ? FitsKind.Cube : FitsKind.Image2D;
        }
        catch
        {
            return FitsKind.NotFits;
        }
    }

    public static bool IsLikelyCube(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return IsLikelyCube(stream);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsLikelyCube(Stream stream)
    {
        try
        {
            var primary = ReadHeader(stream);
            if (primary is null) return false;
            if (Classify(primary) is { } primaryVerdict) return primaryVerdict;

            // Data-less primary (NAXIS=0): the first extension header starts at the next block.
            var extension = ReadHeader(stream);
            return extension is not null && Classify(extension) == true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>true = cube, false = 2D image, null = no data in this HDU (NAXIS 0).</summary>
    private static bool? Classify(Dictionary<string, long> header)
    {
        // fpack tile-compressed HDU: the real image shape lives in ZNAXIS*, not the BINTABLE's NAXIS*.
        if (header.TryGetValue("ZNAXIS", out var znaxis))
            return znaxis >= 3 && header.GetValueOrDefault("ZNAXIS3") > 1;

        var naxis = header.GetValueOrDefault("NAXIS");
        if (naxis == 0) return null;
        if (naxis < 3) return false;
        // A "cube" needs a real third dimension; degenerate NAXIS3=1 (common for imagers) is 2D.
        return header.GetValueOrDefault("NAXIS3") > 1;
    }

    /// <summary>Read one HDU header (blocks up to END), returning the integer-valued cards we care about.</summary>
    private static Dictionary<string, long>? ReadHeader(Stream stream)
    {
        var cards = new Dictionary<string, long>(StringComparer.Ordinal);
        var block = new byte[BlockSize];

        for (var blocks = 0; blocks < MaxHeaderBlocks; blocks++)
        {
            if (!FillBlock(stream, block)) return blocks == 0 ? null : cards; // truncated file
            for (var offset = 0; offset < BlockSize; offset += CardSize)
            {
                var card = Encoding.ASCII.GetString(block, offset, CardSize);
                var keyword = card[..8].TrimEnd();
                if (keyword == "END") return cards;
                if (card.Length > 10 && card[8] == '=' &&
                    (keyword is "NAXIS" or "NAXIS1" or "NAXIS2" or "NAXIS3" or "ZNAXIS" or "ZNAXIS3"))
                {
                    var value = card[10..].Split('/')[0].Trim();
                    if (long.TryParse(value, System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out var v))
                        cards[keyword] = v;
                }
            }
        }
        return cards; // runaway header — return what we have
    }

    private static bool FillBlock(Stream stream, byte[] block)
    {
        var total = 0;
        while (total < block.Length)
        {
            var read = stream.Read(block, total, block.Length - total);
            if (read == 0) return false;
            total += read;
        }
        return true;
    }
}
