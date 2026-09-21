using CanfarDesktop.Models;
using CanfarDesktop.Models.Fits;

namespace CanfarDesktop.Helpers;

/// <summary>
/// Marks, as something that can leave the app.
///
/// <para>A mark is only useful elsewhere if it carries three things: WHERE it is, what it is OF, and
/// how to get that thing again. Inside the app the first is enough, because the file is right there.
/// The moment a mark is written to disk for someone else, a position with no provenance is a number
/// nobody can check.</para>
///
/// <para>Pure, and given everything it needs, so the shape of the output is testable without a
/// viewer, a file or a network. The tool and the toolbar button are both thin callers.</para>
/// </summary>
public static class MarkExport
{
    /// <summary>The schema name written into the file, so a reader can tell what it is holding.</summary>
    public const string Schema = "verbinal.marks/1";

    /// <summary>Where the file came from, when the app knows. Null when it was opened off disk.</summary>
    /// <param name="PublisherId">The IVOA publisher id — the handle that fetches it again.</param>
    public sealed record Provenance(
        string? PublisherId,
        string? Collection,
        string? ObservationId,
        string? Target,
        string? Instrument,
        string? Filter,
        string? StartDate,
        string? CalibrationLevel,
        string? DataRelease,
        string? ProposalId,
        string? ProposalPi,
        string? ProposalTitle,
        string? DownloadedAt,
        string? PreviewUrl,
        string? ThumbnailUrl);

    /// <summary>The image the marks are on.</summary>
    public sealed record Source(
        string LocalPath,
        string FileName,
        int HduIndex,
        string? HduName,
        int Width,
        int Height,
        WcsInfo? Wcs);

    /// <summary>One mark, in every unit a reader might want rather than only the one it was drawn in.</summary>
    public sealed record ExportedMark(
        string Id,
        string Kind,
        string AnchoredIn,
        double? RaDeg,
        double? DecDeg,
        string? Ra,
        string? Dec,
        double? PixelX,
        double? PixelY,
        double? HalfWidthDeg,
        double? HalfWidthArcsec,
        double? HalfWidthPixels,
        string? Text,
        string Colour,
        double Stroke,
        double FontSize,
        bool Bold,
        string Author,
        string? CreatedAt);

    /// <summary>The whole document.</summary>
    public sealed record Document(
        string Schema,
        string ExportedAt,
        string AppVersion,
        Source Source,
        Provenance? Observation,
        IReadOnlyList<ExportedMark> Marks);

    /// <summary>
    /// Build the document.
    ///
    /// <para>Every mark gets BOTH a sky position and a pixel one, whatever it was anchored in, and its
    /// size in degrees, arcseconds and pixels. Whoever opens this should not have to redo the WCS to
    /// use it — and the app has the WCS right here, so making them is the wrong trade.</para>
    ///
    /// <para>The conversions go through <see cref="PixelConvention"/> and <see cref="WcsInfo"/>, the
    /// same pair the viewer draws with, so an exported position and a drawn one cannot disagree.</para>
    /// </summary>
    public static Document Build(
        IReadOnlyList<Annotation> marks,
        Source source,
        Provenance? observation,
        string appVersion,
        DateTime exportedAtUtc)
    {
        var exported = new List<ExportedMark>();

        foreach (var mark in marks ?? [])
        {
            if (!mark.Anchor.IsValid) continue;

            var (ra, dec, x, y) = Locate(mark, source);
            var scale = ArcsecPerPixel(source.Wcs);

            double? halfDeg = null, halfArcsec = null, halfPixels = null;
            if (mark.Extent is { } extent)
            {
                if (mark.Anchor.Space == AnchorSpace.Sky)
                {
                    halfDeg = extent.HalfWidth;
                    halfArcsec = extent.HalfWidth * 3600;
                    halfPixels = scale > 0 ? halfArcsec / scale : null;
                }
                else
                {
                    halfPixels = extent.HalfWidth;
                    halfArcsec = scale > 0 ? extent.HalfWidth * scale : null;
                    halfDeg = halfArcsec / 3600;
                }
            }

            exported.Add(new ExportedMark(
                Id: mark.Id,
                Kind: mark.Kind.ToString().ToLowerInvariant(),
                AnchoredIn: mark.Anchor.Space.ToString().ToLowerInvariant(),
                RaDeg: ra, DecDeg: dec,
                Ra: ra is { } r ? WcsInfo.FormatRa(r) : null,
                Dec: dec is { } d ? WcsInfo.FormatDec(d) : null,
                PixelX: x, PixelY: y,
                HalfWidthDeg: halfDeg, HalfWidthArcsec: halfArcsec, HalfWidthPixels: halfPixels,
                Text: string.IsNullOrWhiteSpace(mark.Text) ? null : mark.Text,
                Colour: mark.EffectiveStyle.ColourHex(),
                Stroke: mark.EffectiveStyle.Stroke,
                FontSize: mark.EffectiveStyle.FontSize,
                Bold: mark.EffectiveStyle.Bold,
                Author: mark.Author.ToString().ToLowerInvariant(),
                CreatedAt: string.IsNullOrWhiteSpace(mark.CreatedAt) ? null : mark.CreatedAt));
        }

        return new Document(
            Schema, exportedAtUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
            appVersion, source, observation, exported);
    }

    /// <summary>
    /// A mark's position both ways round.
    ///
    /// Whichever it was pinned in is exact; the other is derived through the image's WCS, and is null
    /// when there is none — an image with no WCS has pixels and no sky, and inventing one would be
    /// worse than leaving the field out.
    /// </summary>
    private static (double? Ra, double? Dec, double? X, double? Y) Locate(Annotation mark, Source source)
    {
        var wcs = source.Wcs is { IsValid: true } valid ? valid : null;

        if (mark.Anchor.Space == AnchorSpace.Sky)
        {
            var pixel = wcs is null || source.Height <= 0
                ? null
                : PixelConvention.DisplayOfSky(wcs, source.Height, mark.Anchor.X, mark.Anchor.Y);

            return (mark.Anchor.X, mark.Anchor.Y, pixel?.X, pixel?.Y);
        }

        if (mark.Anchor.Space != AnchorSpace.ImagePixel) return (null, null, null, null);

        if (wcs is null || source.Height <= 0) return (null, null, mark.Anchor.X, mark.Anchor.Y);

        var (ra, dec) = PixelConvention.SkyAtDisplay(wcs, source.Height, mark.Anchor.X, mark.Anchor.Y);
        return (ra, dec, mark.Anchor.X, mark.Anchor.Y);
    }

    /// <summary>The image's plate scale, or 0 when it has no WCS to take one from.</summary>
    private static double ArcsecPerPixel(WcsInfo? wcs)
        => wcs is { IsValid: true } valid && valid.PixelScaleArcsec > 0 ? valid.PixelScaleArcsec : 0;
}
