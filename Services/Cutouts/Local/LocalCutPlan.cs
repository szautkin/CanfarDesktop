using System.Globalization;
using CanfarDesktop.Models.Cutouts;
using CanfarDesktop.Services.Fits;

namespace CanfarDesktop.Services.Cutouts.Local;

/// <summary>One image to cut: which, the box of pixels on the sky, and the range kept along every axis.</summary>
public sealed record ImageCut(LocalImage Image, PixelBox Box, IReadOnlyList<AxisRange> Axes)
{
    /// <summary>The cut data's length, before the padding to a whole block.</summary>
    public long DataBytes => Math.Abs((long)Image.Header.BitPix) / 8 * Axes.Aggregate(1L, (n, a) => n * a.Length);
}

/// <summary>One HDU of the cutout file: its header, and the image cut into it when it has one.</summary>
public sealed record CutoutHdu(FitsHeaderCards Header, ImageCut? Data);

/// <summary>
/// What a local cut writes, decided before a byte is: which images the region falls on, the box of
/// each, and every HDU of the new file with its header. Pure — a function of the file's headers and the
/// cutout — so it is tested without writing anything, and its size is exact rather than estimated.
///
/// <para>The new file keeps the old one's shape. A single image is cut into the primary HDU; a
/// multi-extension file keeps its primary header and gets one extension for each image the region
/// touches — the SCI, ERR and DQ of the HST chips it falls on, the MegaPrime CCDs it covers — each with
/// its own box, from its own sky coordinates. Tables, and images the region misses, are left out and
/// named in the primary header's HISTORY.</para>
/// </summary>
public sealed class LocalCutPlan
{
    private LocalCutPlan(IReadOnlyList<CutoutHdu> hdus) => Hdus = hdus;

    /// <summary>The cutout file's HDUs, in order; empty when the region falls on no image.</summary>
    public IReadOnlyList<CutoutHdu> Hdus { get; }

    public IEnumerable<ImageCut> Cuts => Hdus.Select(h => h.Data).OfType<ImageCut>();

    public bool IsEmpty => !Cuts.Any();

    /// <summary>Exactly how large the cutout file will be.</summary>
    public long Bytes => Hdus.Sum(h => h.Header.ToBytes().Length + FitsParser.AlignToBlock(h.Data?.DataBytes ?? 0));

    public static LocalCutPlan For(LocalFitsFile file, CutoutSpec spec, DateTime? now = null)
    {
        var cuts = file.Images
            .Select(image => (Image: image, Box: BoxOn(image, spec.Region)))
            .Where(c => c.Box is not null)
            .Select(c => new ImageCut(c.Image, c.Box!.Value, AxesOf(c.Image, c.Box.Value)))
            .ToList();
        if (cuts.Count == 0) return new LocalCutPlan([]);

        var when = (now ?? DateTime.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var region = spec.Region is { } r
            ? $"Region (ICRS, degrees): {r.SodaParameter} {r.ToSoda()}"
            : "Region: the whole image";

        var hdus = new List<CutoutHdu>();
        var primaryCut = cuts.FirstOrDefault(c => c.Image.Hdu.Index == 0);
        if (primaryCut is null)
        {
            var cutIndices = cuts.Select(c => c.Image.Hdu.Index).ToHashSet();
            var left = file.Hdus.Skip(1).Where(h => !cutIndices.Contains(h.Index)).Select(h => h.Label).ToList();
            var dataless = file.Hdus.Count > 0 && file.Hdus[0].DataBytes == 0 ? file.Hdus[0] : null;
            var history = new List<string> { $"Cut out by Verbinal on {when} from {file.FileName}: {cuts.Count} of its images." };
            if (left.Count > 0) history.Add($"Left out (not images, or the region is not on them): {string.Join(" ", left)}");
            history.Add(region);
            hdus.Add(new CutoutHdu(CutoutHeader.ForPrimary(dataless, cuts.Count, history), null));
        }

        foreach (var cut in cuts)
        {
            var header = CutoutHeader.ForCut(cut.Image.Encoding.ImageCards(cut.Image.Hdu), cut.Axes,
            [
                $"Cut out by Verbinal on {when} from {file.FileName}{cut.Image.Hdu.Label}, pixels {cut.Box}.",
                region,
            ]);
            if (cut == primaryCut)
            {
                // Its extensions are now only the ones cut with it.
                if (header.Contains("NEXTEND")) header.Set("NEXTEND", (long)cuts.Count - 1);
                if (cuts.Count > 1 && !header.Contains("EXTEND")) header.Set("EXTEND", true);
            }
            hdus.Add(new CutoutHdu(header, cut));
        }
        return new LocalCutPlan(hdus);
    }

    /// <summary>The pixels the region covers on this image — all of it when the cutout has no region.</summary>
    private static PixelBox? BoxOn(LocalImage image, SkyRegion? region)
        => region is null
            ? new PixelBox(1, 1, image.Width, image.Height)
            : PixelBox.Around(region, image.Wcs, image.Width, image.Height);

    /// <summary>The box on the two sky axes; every further axis (a cube's planes, a Stokes axis) whole.</summary>
    private static IReadOnlyList<AxisRange> AxesOf(LocalImage image, PixelBox box)
    {
        var axes = new List<AxisRange> { new(box.X0 - 1, box.Width), new(box.Y0 - 1, box.Height) };
        for (var n = 3; n <= image.Header.NAxis; n++) axes.Add(new AxisRange(0, image.Header.GetInt($"NAXIS{n}")));
        return axes;
    }
}
