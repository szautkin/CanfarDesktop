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

/// <summary>
/// The shape a file presents to the viewers. <see cref="HasCubeAxis"/> = a real third image axis
/// (NAXIS3&gt;1) exists, so the Cube Viewer CAN open it. <see cref="RecommendCube"/> = that axis is
/// spectral (CTYPE3 = FREQ/WAVE/VELO/…), so the Cube Viewer is the RIGHT default — a detector/chip
/// stack (e.g. WFPC2 c0f, no spectral CTYPE3) is a cube by shape but is best viewed 2D.
/// </summary>
public readonly record struct FitsShape(FitsKind Kind, bool IsSpectral)
{
    public bool HasCubeAxis => Kind == FitsKind.Cube;
    public bool RecommendCube => Kind == FitsKind.Cube && IsSpectral;
}

public static class FitsSniff
{
    private const int BlockSize = 2880;
    private const int CardSize = 80;
    private const int MaxHeaderBlocks = 64; // defensive cap per HDU header

    // FITS WCS Paper III spectral algorithm codes (CTYPE3 prefix). CGPS uses "VELO-LSR".
    private static readonly string[] SpectralCodes =
        { "FREQ", "ENER", "WAVN", "VRAD", "WAVE", "VOPT", "ZOPT", "AWAV", "VELO", "BETA", "FELO", "VELOCITY" };

    /// <summary>
    /// Inspect a file's shape (content-based — a mis-served download can put FITS bytes in a ".png",
    /// so magic-check first). Distinguishes a spectral cube (recommend Cube Viewer) from a 2D image
    /// or a non-spectral detector stack (recommend FITS Viewer, but the cube axis is still offerable).
    /// </summary>
    public static FitsShape Inspect(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var magic = new byte[9];
            if (stream.Read(magic, 0, 9) < 9 ||
                !Encoding.ASCII.GetString(magic).StartsWith("SIMPLE  =", StringComparison.Ordinal))
                return new FitsShape(FitsKind.NotFits, false);
            stream.Position = 0;
            return Inspect(stream);
        }
        catch
        {
            return new FitsShape(FitsKind.NotFits, false);
        }
    }

    /// <summary>Shape inspection over an already-open stream (no magic check — the caller owns that).</summary>
    public static FitsShape Inspect(Stream stream)
    {
        try
        {
            var h = ReadHeader(stream);
            if (h is null) return new FitsShape(FitsKind.NotFits, false);
            var verdict = Classify(h.Nums);
            if (verdict is null) // data-less primary → follow to the first extension
            {
                h = ReadHeader(stream);
                if (h is null) return new FitsShape(FitsKind.Image2D, false);
                verdict = Classify(h.Nums);
            }
            var kind = verdict == true ? FitsKind.Cube : FitsKind.Image2D;
            var spectral = kind == FitsKind.Cube && IsSpectralAxis(h.CType3);
            return new FitsShape(kind, spectral);
        }
        catch
        {
            return new FitsShape(FitsKind.NotFits, false);
        }
    }

    /// <summary>
    /// Content-based classification (extension lies: a mis-served download can put FITS bytes in a
    /// ".png"). Magic check first — every FITS starts with the literal card "SIMPLE  =" — then the
    /// cube-vs-2D shape sniff.
    /// </summary>
    public static FitsKind ClassifyFile(string path) => Inspect(path).Kind;

    private static bool IsSpectralAxis(string? ctype3)
    {
        if (string.IsNullOrWhiteSpace(ctype3)) return false;
        var code = ctype3.Trim();
        foreach (var c in SpectralCodes)
            if (code.StartsWith(c, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
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
            if (Classify(primary.Nums) is { } primaryVerdict) return primaryVerdict;

            // Data-less primary (NAXIS=0): the first extension header starts at the next block.
            var extension = ReadHeader(stream);
            return extension is not null && Classify(extension.Nums) == true;
        }
        catch
        {
            return false;
        }
    }

    private sealed record HeaderInfo(Dictionary<string, long> Nums, string? CType3);

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

    /// <summary>Read one HDU header (blocks up to END), returning the integer axis cards plus the
    /// CTYPE3 string (the third-axis type, for spectral-vs-detector classification).</summary>
    private static HeaderInfo? ReadHeader(Stream stream)
    {
        var cards = new Dictionary<string, long>(StringComparer.Ordinal);
        string? ctype3 = null;
        var block = new byte[BlockSize];

        for (var blocks = 0; blocks < MaxHeaderBlocks; blocks++)
        {
            if (!FillBlock(stream, block)) return blocks == 0 ? null : new HeaderInfo(cards, ctype3); // truncated
            for (var offset = 0; offset < BlockSize; offset += CardSize)
            {
                var card = Encoding.ASCII.GetString(block, offset, CardSize);
                var keyword = card[..8].TrimEnd();
                if (keyword == "END") return new HeaderInfo(cards, ctype3);
                if (card.Length <= 10 || card[8] != '=') continue;

                if (keyword is "NAXIS" or "NAXIS1" or "NAXIS2" or "NAXIS3" or "ZNAXIS" or "ZNAXIS3")
                {
                    var value = card[10..].Split('/')[0].Trim();
                    if (long.TryParse(value, System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out var v))
                        cards[keyword] = v;
                }
                else if (keyword is "CTYPE3" or "ZCTYPE3")
                {
                    // String card: value sits between single quotes, e.g. CTYPE3 = 'VELO-LSR  '
                    var raw = card[10..].Split('/')[0].Trim();
                    ctype3 = raw.Trim('\'').Trim();
                }
            }
        }
        return new HeaderInfo(cards, ctype3); // runaway header — return what we have
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
