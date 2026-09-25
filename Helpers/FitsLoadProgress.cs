namespace CanfarDesktop.Helpers;

/// <summary>
/// How far through a FITS file the parser has got.
///
/// <para>Reported per HDU, because that is the grain the work actually happens at and the grain a
/// person can see moving. A mosaic frame has forty-one extensions and each one is decompressed in
/// turn; on the file this was written for that is over half a minute, during which the viewer showed
/// a twenty-pixel spinner and nothing else. A spinner says "something is happening". It does not say
/// whether it is nearly done, and it does not say whether it is stuck.</para>
/// </summary>
/// <param name="HdusParsed">How many extensions are finished.</param>
/// <param name="BytesRead">Where the parser has reached in the file.</param>
/// <param name="TotalBytes">The file's length, or 0 when the stream cannot say.</param>
/// <param name="CurrentHdu">The extension being read, by name, when it has one.</param>
public readonly record struct FitsParseProgress(
    int HdusParsed,
    long BytesRead,
    long TotalBytes,
    string? CurrentHdu);

/// <summary>
/// Turning that into something to show.
///
/// <para>Bytes rather than extensions for the bar, because the count of extensions is not known until
/// the file has been read — a progress bar that discovers its own maximum as it goes runs backwards.
/// The file's length is known from the first moment.</para>
/// </summary>
public static class FitsLoadProgress
{
    /// <summary>
    /// How far through, from 0 to 1. Null when the file's length is unknown, which is a real state —
    /// a stream that cannot be measured should leave the bar indeterminate rather than guess.
    /// </summary>
    public static double? Fraction(FitsParseProgress progress)
    {
        if (progress.TotalBytes <= 0) return null;
        if (!IsSane(progress.BytesRead)) return null;

        return Math.Clamp((double)progress.BytesRead / progress.TotalBytes, 0, 1);
    }

    /// <summary>
    /// What to say while it happens.
    ///
    /// <para>Names the extension being read when the file has named ones, because on a mosaic that is
    /// the difference between "still going" and "on ccd34 of what looks like forty". Falls back to the
    /// count, and then to the file, so there is always something truthful to show.</para>
    /// </summary>
    public static string Describe(string fileName, FitsParseProgress progress)
    {
        var name = progress.CurrentHdu?.Trim();

        if (!string.IsNullOrEmpty(name))
            return progress.HdusParsed > 0
                ? $"Reading {name} — {progress.HdusParsed} done"
                : $"Reading {name}";

        if (progress.HdusParsed > 0)
            return $"Reading {fileName} — {progress.HdusParsed} extensions";

        return $"Reading {fileName}";
    }

    private static bool IsSane(long value) => value >= 0;
}
