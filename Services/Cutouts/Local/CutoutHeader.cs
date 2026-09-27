using System.Globalization;
using System.Text.RegularExpressions;
using CanfarDesktop.Models.Fits;
using CanfarDesktop.Services.Fits;

namespace CanfarDesktop.Services.Cutouts.Local;

/// <summary>Part of one image axis: from <see cref="Start"/> (0-based) for <see cref="Length"/> pixels.</summary>
public readonly record struct AxisRange(long Start, long Length);

/// <summary>
/// The header of a cutout: the image's own, with what the cut changes changed and nothing else.
///
/// <para>Every keyword a cut has to touch is in <see cref="Rules"/>, and only there: the size, the
/// reference pixel of every WCS the header carries, IRAF's physical offset, the pixel sections a
/// detector header names, and the checksums the new data no longer matches. The WCS matrix, the SIP
/// distortion (measured from the reference pixel, so still right once it moves) and BITPIX, BSCALE,
/// BZERO and BLANK are left as they are — the pixels are copied, not converted.</para>
///
/// <para>Two things are added. Where the cut sits in the file it came from, as IRAF's LTV and LTM, when
/// the file did not say already: DS9 and IRAF then show the file's own pixel numbers ("physical") on
/// the cut — which is how a MegaPipe catalogue, in tile pixels, is matched to it. And a fresh CHECKSUM
/// and DATASUM, reserved here and sealed by the cutter once the data is summed.</para>
/// </summary>
public static partial class CutoutHeader
{
    private delegate void Rule(FitsHeaderCards cards, string keyword, int axis, FitsHeader before, IReadOnlyList<AxisRange> axes);

    /// <summary>What a cut does to a header, keyword by keyword. A keyword no rule names is kept as it was.</summary>
    private static readonly (Regex Keyword, Rule Apply)[] Rules =
    [
        // The image's length along each axis.
        (AxisLength(), (cards, key, axis, before, axes) =>
        {
            if (before.GetInt(key) != axes[axis - 1].Length) cards.Set(key, axes[axis - 1].Length);
        }),

        // The reference pixel — of the main WCS and of every alternate (CRPIX1A…, HST's CRPIX1O) —
        // counted from the cut's corner now.
        (ReferencePixel(), Shift),

        // IRAF's image ↔ physical mapping (image = LTM × physical + LTV): its offset moves with the corner.
        (PhysicalOffset(), Shift),

        // Sections of this image — where its data, overscan and trimmed area are — moved and clipped to
        // the cut, or dropped when the cut misses them.
        (ImageSection(), ClipSection),

        // Sections in the DETECTOR's pixels described the whole frame, and would now be wrong.
        (DetectorSection(), (cards, key, _, _, _) => cards.Remove(key)),

        // A checksum of the old data is a false one for the new: dropped here, a fresh one sealed on writing.
        (Checksum(), (cards, key, _, _, _) => cards.Remove(key)),
    ];

    /// <summary>The header of an image cut to <paramref name="axes"/> (one range per axis), with its provenance.</summary>
    public static FitsHeaderCards ForCut(FitsHeaderCards image, IReadOnlyList<AxisRange> axes, IEnumerable<string> history)
    {
        var cards = new FitsHeaderCards(image.Cards);
        var before = cards.Parse();
        foreach (var keyword in cards.Cards.Select(FitsHeaderCards.KeywordOf).Distinct().ToList())
        {
            foreach (var (pattern, apply) in Rules)
            {
                var match = pattern.Match(keyword);
                if (!match.Success) continue;
                var axis = match.Groups["axis"].Success ? int.Parse(match.Groups["axis"].Value, CultureInfo.InvariantCulture) : 0;
                if (match.Groups["axis"].Success && (axis < 1 || axis > axes.Count)) break;
                apply(cards, keyword, axis, before, axes);
                break;
            }
        }
        AddPhysicalMapping(cards, before, axes);
        foreach (var line in history) cards.AddHistory(line);
        FitsChecksum.Reserve(cards);
        return cards;
    }

    /// <summary>
    /// Where the cut sits in the file it came from, when the file did not say: physical = (image − LTV) / LTM,
    /// so a cut starting at the file's pixel 2731 has LTV1 = −2730. An LTV the file had is moved by
    /// <see cref="Rules"/> instead, keeping whatever it measured from.
    /// </summary>
    private static void AddPhysicalMapping(FitsHeaderCards cards, FitsHeader before, IReadOnlyList<AxisRange> axes)
    {
        var added = false;
        for (var n = 1; n <= Math.Min(2, axes.Count); n++)
        {
            if (before.Contains($"LTV{n}") || axes[n - 1].Start == 0) continue;
            cards.Set($"LTV{n}", (double)-axes[n - 1].Start, "image = physical - offset: pixels of the file cut from");
            added = true;
        }
        if (!added || new[] { "LTM1_1", "LTM1_2", "LTM2_1", "LTM2_2" }.Any(before.Contains)) return;
        cards.Set("LTM1_1", 1.0, "physical to image scale");
        cards.Set("LTM2_2", 1.0, "physical to image scale");
    }

    /// <summary>
    /// The primary header of a cutout whose images are extensions: the file's own when it holds no data
    /// (the usual multi-extension layout), with NEXTEND made true; otherwise a bare one.
    /// </summary>
    public static FitsHeaderCards ForPrimary(FitsHduLayout? dataless, int extensions, IEnumerable<string> history)
    {
        FitsHeaderCards cards;
        if (dataless is not null)
        {
            cards = new FitsHeaderCards(dataless.RawCards);
            if (cards.Contains("NEXTEND")) cards.Set("NEXTEND", extensions);
        }
        else
        {
            cards = new FitsHeaderCards([]);
            cards.Set("SIMPLE", true, "conforms to FITS standard");
            cards.Set("BITPIX", 8L);
            cards.Set("NAXIS", 0L, "no data: the images are extensions");
            cards.Set("EXTEND", true);
        }
        foreach (var line in history) cards.AddHistory(line);
        FitsChecksum.Reserve(cards);
        return cards;
    }

    private static void Shift(FitsHeaderCards cards, string keyword, int axis, FitsHeader before, IReadOnlyList<AxisRange> axes)
    {
        if (axes[axis - 1].Start == 0) return; // nothing moved: keep the card as the pipeline wrote it
        cards.Set(keyword, before.GetDouble(keyword) - axes[axis - 1].Start);
    }

    /// <summary>"[33:2080,1:4612]" in this image's pixels, clipped to the cut and counted from its corner.</summary>
    private static void ClipSection(FitsHeaderCards cards, string keyword, int _, FitsHeader before, IReadOnlyList<AxisRange> axes)
    {
        var match = Section().Match(before.GetString(keyword) ?? "");
        if (!match.Success || axes.Count < 2) { cards.Remove(keyword); return; }

        var clipped = new List<string>();
        for (var i = 0; i < 2; i++)
        {
            var a = long.Parse(match.Groups[2 * i + 1].Value, CultureInfo.InvariantCulture);
            var b = long.Parse(match.Groups[2 * i + 2].Value, CultureInfo.InvariantCulture);
            var (lo, hi) = (Math.Min(a, b), Math.Max(a, b));
            var from = Math.Max(lo, axes[i].Start + 1) - axes[i].Start;
            var to = Math.Min(hi, axes[i].Start + axes[i].Length) - axes[i].Start;
            if (from > to) { cards.Remove(keyword); return; }
            clipped.Add(a <= b ? $"{from}:{to}" : $"{to}:{from}"); // a section read backwards stays backwards
        }
        cards.SetString(keyword, $"[{clipped[0]},{clipped[1]}]");
    }

    [GeneratedRegex(@"^NAXIS(?<axis>\d+)$")]
    private static partial Regex AxisLength();

    [GeneratedRegex(@"^CRPIX(?<axis>\d)[A-Z]?$")]
    private static partial Regex ReferencePixel();

    [GeneratedRegex(@"^LTV(?<axis>\d)$")]
    private static partial Regex PhysicalOffset();

    [GeneratedRegex(@"^(DATASEC|TRIMSEC|BIASSEC|DSEC[A-Z0-9]*|TSEC[A-Z0-9]*|BSEC[A-Z0-9]*)$")]
    private static partial Regex ImageSection();

    [GeneratedRegex(@"^(DETSEC|CCDSEC|AMPSEC|DETSEC[A-Z0-9]+|CSEC[A-Z0-9]*|ASEC[A-Z0-9]*)$")]
    private static partial Regex DetectorSection();

    [GeneratedRegex(@"^(CHECKSUM|DATASUM)$")]
    private static partial Regex Checksum();

    [GeneratedRegex(@"^\s*\[\s*(\d+)\s*:\s*(\d+)\s*,\s*(\d+)\s*:\s*(\d+)\s*\]\s*$")]
    private static partial Regex Section();
}
