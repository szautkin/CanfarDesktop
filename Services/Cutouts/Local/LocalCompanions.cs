using CanfarDesktop.Helpers;
using CanfarDesktop.Models.Cutouts;

namespace CanfarDesktop.Services.Cutouts.Local;

/// <summary>
/// Another of the observation's files, beside the one cut on this computer, and which of its images lies
/// on the same pixels as each of the cut file's — or why it cannot be cut with it.
/// </summary>
/// <param name="Images">For each image of the cut file, by its HDU index, the companion's image on the same pixels; empty when it is unavailable.</param>
public sealed record LocalCompanion(string ArtifactId, LocalFitsFile File, IReadOnlyDictionary<int, LocalImage> Images, string? Unavailable)
{
    /// <summary>What a cutout editor or an agent is offered.</summary>
    public CutoutCompanion Offer => new(ArtifactId, File.FileName, Unavailable);
}

/// <summary>
/// The observation's other files that can be cut with the one on this computer, box for box — a
/// MegaPipe tile's weight map, which MegaPipe makes on the tile's own pixels and CADC does not cut.
///
/// <para>A companion is not guessed at: it is one of the observation's own files, by the name the
/// archive gives it, in the same folder as the file cut. And the same box is only the same sky if the
/// two lie on the same pixels, which is checked rather than assumed — the same shape, and every pixel
/// of a grid across the image at the same place on the sky to a hundredth of a pixel, the same
/// wavelength for a cube's planes. A file that is not is offered greyed, with why.</para>
/// </summary>
public static class LocalCompanions
{
    /// <summary>How far apart the two can put one pixel, in pixels, and still be the same grid.</summary>
    public const double Tolerance = 0.01;

    /// <summary>
    /// Those of <paramref name="artifactIds"/> — the observation's files — that are FITS and are beside
    /// <paramref name="file"/>, other than itself, each read and matched. Reads their headers: run it off the UI.
    /// </summary>
    public static IReadOnlyList<LocalCompanion> Beside(LocalFitsFile file, IEnumerable<string> artifactIds)
    {
        var folder = Path.GetDirectoryName(file.Path);
        if (string.IsNullOrEmpty(folder) || file.Images.Count == 0) return [];

        var companions = new List<LocalCompanion>();
        foreach (var id in artifactIds.Distinct())
        {
            var name = Caom2Format.ArtifactFileName(id);
            if (id == file.ArtifactId || !CutoutCandidates.IsFitsFile(null, name)
                || string.Equals(name, file.FileName, StringComparison.OrdinalIgnoreCase)) continue;
            var path = Path.Combine(folder, name);
            if (!System.IO.File.Exists(path)) continue;
            companions.Add(Match(file, LocalFitsFile.Inspect(path, id)));
        }
        return companions;
    }

    /// <summary>
    /// The companion's image on the same pixels as each of the file's images: the one image of each,
    /// when each has one; otherwise the one of the same name (SCI,1 with SCI,1, ccd07 with ccd07).
    /// Unavailable, with why, when any of the file's images has none.
    /// </summary>
    public static LocalCompanion Match(LocalFitsFile file, LocalFitsFile companion)
    {
        LocalCompanion Refused(string why) => new(companion.ArtifactId, companion, new Dictionary<int, LocalImage>(), why);

        if (companion.Problem is { } problem) return Refused(problem);

        var images = new Dictionary<int, LocalImage>();
        foreach (var image in file.Images)
        {
            var twin = file.Images.Count == 1 && companion.Images.Count == 1
                ? companion.Images[0]
                : companion.Images.FirstOrDefault(c => c.Hdu.Id == image.Hdu.Id);
            if (twin is null)
                return Refused(string.Format(CutoutRules.T("Cutout_CompanionNoImage", "It has no image {0}, as the file cut has."),
                    image.Hdu.Label));
            if (!SameGrid(image, twin))
                return Refused(string.Format(CutoutRules.T("Cutout_CompanionOtherGrid",
                    "Its image {0} is not on the same pixels as the file cut, so the same box would not be the same sky."),
                    twin.Hdu.Label));
            images[image.Hdu.Index] = twin;
        }
        return new LocalCompanion(companion.ArtifactId, companion, images, null);
    }

    /// <summary>
    /// Whether the two images lie on the same pixels: the same size along every axis, the same place on
    /// the sky at each point of a 3 × 3 grid across the image — corners, edges' middles, centre — to a
    /// hundredth of a pixel, and a cube's planes at the same wavelengths.
    ///
    /// <para>Compared as the sky each pixel is at, not card by card: the same grid can be written with a
    /// CD matrix in one file and PC and CDELT in the other. Only the way from pixel to sky is used, which
    /// is exact; the way back can be a fitted approximation (SIP's AP and BP).</para>
    /// </summary>
    public static bool SameGrid(LocalImage a, LocalImage b)
    {
        if (a.Header.NAxis != b.Header.NAxis) return false;
        for (var n = 1; n <= a.Header.NAxis; n++)
            if (a.Header.GetInt($"NAXIS{n}") != b.Header.GetInt($"NAXIS{n}")) return false;

        var scale = Math.Sqrt(Math.Abs(a.Wcs.Cd1_1 * a.Wcs.Cd2_2 - a.Wcs.Cd1_2 * a.Wcs.Cd2_1)); // degrees per pixel
        foreach (var x in new[] { 1.0, (a.Width + 1) / 2.0, a.Width })
            foreach (var y in new[] { 1.0, (a.Height + 1) / 2.0, a.Height })
            {
                var (ra1, dec1) = a.Wcs.PixelToWorld(x, y);
                var (ra2, dec2) = b.Wcs.PixelToWorld(x, y);
                var apart = SkyGeometry.Distance(new SkyPoint(ra1, dec1), new SkyPoint(ra2, dec2));
                if (!(apart <= Tolerance * scale)) return false; // NaN too
            }

        if (a.Spectral is not { } s) return true;
        if (b.Spectral is not { } t || s.Axis != t.Axis) return false;
        var channel = s.Range is { } range ? (range.Max - range.Min) / s.Length : double.NaN;
        return Near(s.WavelengthAt(0.5), t.WavelengthAt(0.5), Tolerance * channel)
               && Near(s.WavelengthAt(s.Length + 0.5), t.WavelengthAt(t.Length + 0.5), Tolerance * channel);
    }

    private static bool Near(double? a, double? b, double within)
        => a is { } x && b is { } y && Math.Abs(x - y) <= within;
}
