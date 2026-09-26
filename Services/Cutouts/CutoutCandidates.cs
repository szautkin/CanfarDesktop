namespace CanfarDesktop.Services.Cutouts;

/// <summary>
/// Which of an observation's files a cutout could even be asked of.
///
/// <para>SODA cuts FITS images and cubes. A preview JPEG, a thumbnail, a source catalogue or a tar
/// package is not a thing with a region to cut, so offering "Cutout…" on one — even greyed — says the
/// app does not know what a cutout is. Among FITS files, whether CADC WILL cut one is DataLink's to say
/// (its cutout service), and a file it offers none for is shown greyed with that reason: HST's mirrored
/// frames, for one, are refused by SODA even at their own target.</para>
/// </summary>
public static class CutoutCandidates
{
    private static readonly string[] FitsEndings = [".fits", ".fit", ".fts", ".fits.fz", ".fits.gz", ".fz"];

    /// <summary>A FITS file by its type or its name, and not a preview or thumbnail of one.</summary>
    public static bool IsFitsFile(string? contentType, string? uri, string? productType = null)
    {
        if (productType?.ToLowerInvariant() is "preview" or "thumbnail") return false;
        if (contentType?.Contains("fits", StringComparison.OrdinalIgnoreCase) == true) return true;

        var name = uri ?? string.Empty;
        var query = name.IndexOf('?');
        if (query >= 0) name = name[..query];
        return FitsEndings.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// What CADC would cut when this file was chosen: the file of that name — or, only when none was
    /// chosen by name, the one file it cuts. A file chosen that CADC does not cut has nothing: a weight
    /// map or a preview picked from the list is downloaded whole, never as a cutout of the science image.
    /// </summary>
    public static Models.Cutouts.SodaDescriptor? CutFor(IReadOnlyList<Models.Cutouts.SodaDescriptor> cutouts, string? chosenFileName)
        => string.IsNullOrEmpty(chosenFileName)
            ? (cutouts.Count == 1 ? cutouts[0] : null)
            : cutouts.FirstOrDefault(c => string.Equals(c.FileName, chosenFileName, StringComparison.OrdinalIgnoreCase));
}
