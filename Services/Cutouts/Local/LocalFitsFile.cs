using CanfarDesktop.Helpers;
using CanfarDesktop.Models.Cutouts;
using CanfarDesktop.Models.Fits;
using CanfarDesktop.Services.Fits;

namespace CanfarDesktop.Services.Cutouts.Local;

/// <summary>One image in a file on this computer that can be cut on the sky: where it lies, and how to read it.</summary>
/// <param name="Header">Its header as a plain image has it — what its size and sky coordinates are read from.</param>
/// <param name="Spectral">A cube's spectral axis, when it has one that can be read as wavelength.</param>
public sealed record LocalImage(FitsHduLayout Hdu, IFitsImageEncoding Encoding, FitsHeader Header, WcsInfo Wcs, SkyRegion Footprint,
                                SpectralAxis? Spectral = null)
{
    public int Width => Header.NAxis1;
    public int Height => Header.NAxis2;
}

/// <summary>
/// A FITS file on this computer, as a cutout sees it: the images in it that can be cut on the sky,
/// each with its own sky coordinates and footprint — the chips of an HST frame, the CCDs of a mosaic —
/// and, when none can be, why not.
///
/// <para>Read from the headers alone; not a pixel is touched until the cut. What it can be cut by is
/// what its header says, as <see cref="SodaDescriptor"/> is what DataLink says — so the same editor, the
/// same <see cref="CutoutRules"/> and the same suggestion serve both.</para>
/// </summary>
public sealed class LocalFitsFile : ICutoutFile
{

    private LocalFitsFile(string path, string artifactId, long fileBytes, IReadOnlyList<FitsHduLayout> hdus,
                          IReadOnlyList<LocalImage> images, string? problem)
    {
        Path = path;
        ArtifactId = artifactId;
        FileBytes = fileBytes;
        Hdus = hdus;
        Images = images;
        Problem = problem;
        var bands = images.Select(i => i.Spectral?.Range).OfType<(double Min, double Max)>().ToList();
        if (bands.Count > 0) (BandMin, BandMax) = (bands.Min(b => b.Min), bands.Max(b => b.Max));
        Parameters = images.Count == 0 ? new HashSet<string>()
            : bands.Count > 0 ? new HashSet<string> { "CIRCLE", "POLYGON", "BAND" }
            : new HashSet<string> { "CIRCLE", "POLYGON" };
        Parts = images.Count > 1 ? images.Select(i => i.Footprint).ToList() : [];
        Footprint = images.Count switch
        {
            0 => null,
            1 => images[0].Footprint,
            _ => SkyRegion.Polygon(SkyGeometry.ConvexHull(images.SelectMany(i => i.Footprint.Vertices).ToList())),
        };
    }

    /// <summary>Where the file is.</summary>
    public string Path { get; }

    /// <summary>The archive file it is a copy of; empty when that is not known.</summary>
    public string ArtifactId { get; }

    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>Its size on disk.</summary>
    public long FileBytes { get; }

    /// <summary>Every HDU in it, image or not.</summary>
    public IReadOnlyList<FitsHduLayout> Hdus { get; }

    /// <summary>The images in it that can be cut on the sky, in file order.</summary>
    public IReadOnlyList<LocalImage> Images { get; }

    /// <summary>Why nothing in it can be cut, when nothing can; null when something can.</summary>
    public string? Problem { get; }

    public SkyRegion? Footprint { get; }
    public SkyRegion? BoundingCircle => null;
    /// <summary>The wavelengths its cubes cover, metres — the planes' outer edges.</summary>
    public double? BandMin { get; }
    public double? BandMax { get; }
    public double? TimeMin => null;
    public double? TimeMax => null;
    public IReadOnlyList<string> PolStates => [];
    public IReadOnlySet<string> Parameters { get; }
    public IReadOnlyList<SkyRegion> Parts { get; }

    /// <summary>
    /// Read a file's headers — through the same unwrapping the FITS viewer uses, so a .fits.gz or a tar
    /// package opens as it does there. Never throws: a file that cannot be read says why in its
    /// <see cref="Problem"/>.
    /// </summary>
    public static LocalFitsFile Inspect(string path, string artifactId)
    {
        try
        {
            var fileBytes = new FileInfo(path).Length;
            using var stream = FitsContainer.OpenFits(path);
            return From(FitsLayout.Read(stream), stream.Length, path, artifactId, fileBytes);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
                                       or NotSupportedException or ArgumentException)
        {
            return new LocalFitsFile(path, artifactId, 0, [], [],
                string.Format(CutoutRules.T("Cutout_LocalUnreadable", "The downloaded file cannot be read as FITS: {0}"), ex.Message));
        }
    }

    /// <summary>The file as its HDUs describe it. <paramref name="streamLength"/> is the FITS data's own length, unwrapped.</summary>
    public static LocalFitsFile From(IReadOnlyList<FitsHduLayout> hdus, long streamLength, string path, string artifactId, long fileBytes)
    {
        var images = new List<LocalImage>();
        var reasons = new List<string>();
        foreach (var hdu in hdus)
        {
            if (FitsImageEncodings.For(hdu.Header) is not { } encoding) continue; // a table, or a header alone
            if (encoding.Refusal(hdu.Header) is { } refused) { reasons.Add(refused); continue; }
            if (hdu.DataStart + hdu.DataBytes > streamLength)
            {
                reasons.Add(CutoutRules.T("Cutout_LocalTruncated",
                    "The downloaded file ends before its image does; it may not have finished downloading."));
                continue;
            }

            var header = encoding.ImageCards(hdu).Parse();
            if (SkyProblem(header) is { } problem) { reasons.Add(problem); continue; }

            var wcs = WcsInfo.FromHeader(header);
            images.Add(new LocalImage(hdu, encoding, header, wcs, FootprintOf(wcs, header.NAxis1, header.NAxis2),
                SpectralAxis.Find(header)));
        }

        var why = images.Count > 0 ? null
            : reasons.FirstOrDefault() ?? CutoutRules.T("Cutout_LocalNoImage", "The downloaded file holds no image to cut.");
        return new LocalFitsFile(path, artifactId, fileBytes, hdus, images, why);
    }

    /// <summary>
    /// Why a region in RA and Dec cannot be placed on this image, when it cannot. A region is ICRS
    /// degrees; an image must say, in real WCS keywords, where its pixels are in RA and Dec.
    /// </summary>
    private static string? SkyProblem(FitsHeader header)
    {
        var wcs = WcsInfo.FromHeader(header);
        if (wcs.IsApproximate)
            return CutoutRules.T("Cutout_LocalApproximateWcs",
                "The downloaded file's sky coordinates are only approximate — rebuilt from its RA and DEC keywords — too rough to cut by.");

        var ctype1 = (header.GetString("CTYPE1") ?? "").Trim();
        var ctype2 = (header.GetString("CTYPE2") ?? "").Trim();
        var determinant = wcs.Cd1_1 * wcs.Cd2_2 - wcs.Cd1_2 * wcs.Cd2_1;
        if (ctype1.Length == 0 || ctype2.Length == 0 || !double.IsFinite(determinant) || Math.Abs(determinant) < 1e-30)
            return CutoutRules.T("Cutout_LocalNoWcs",
                "The downloaded file's image has no sky coordinates, so a region on the sky cannot be placed on it.");

        if (!ctype1.StartsWith("RA", StringComparison.OrdinalIgnoreCase) || !ctype2.StartsWith("DEC", StringComparison.OrdinalIgnoreCase))
            return string.Format(CutoutRules.T("Cutout_LocalNotRaDec",
                "The downloaded file's image is in {0} and {1}, not RA and Dec, so a region in RA and Dec cannot be placed on it."),
                ctype1.Split('-')[0], ctype2.Split('-')[0]);

        // FITS Paper II §3.1: RADESYS names the frame; without it, an EQUINOX before 1984 means FK4.
        var frame = (header.GetString("RADESYS") ?? header.GetString("RADECSYS") ?? "").Trim().ToUpperInvariant();
        var fk4 = frame.StartsWith("FK4", StringComparison.Ordinal)
                  || (frame.Length == 0 && header.Contains("EQUINOX") && header.GetDouble("EQUINOX") < 1984);
        return fk4
            ? CutoutRules.T("Cutout_LocalFk4",
                "The downloaded file's coordinates are B1950 (FK4); a cutout's region is ICRS, and the app does not convert between them.")
            : null;
    }

    /// <summary>
    /// Where the image lies: its outer edge, four points to a side so a distorted edge is followed, as a
    /// polygon on the sky wound as CADC winds its footprints.
    /// </summary>
    private static SkyRegion FootprintOf(WcsInfo wcs, int width, int height)
    {
        const int perSide = 4;
        (double X, double Y)[] corners = [(0.5, 0.5), (width + 0.5, 0.5), (width + 0.5, height + 0.5), (0.5, height + 0.5)];
        var outline = new List<SkyPoint>();
        for (var side = 0; side < corners.Length; side++)
        {
            var (a, b) = (corners[side], corners[(side + 1) % corners.Length]);
            for (var k = 0; k < perSide; k++)
            {
                var t = (double)k / perSide;
                var (ra, dec) = wcs.PixelToWorld(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y));
                outline.Add(new SkyPoint(ra, dec));
            }
        }
        return SkyRegion.Polygon(outline);
    }
}
