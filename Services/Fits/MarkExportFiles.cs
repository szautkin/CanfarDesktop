using CanfarDesktop.Helpers;
using CanfarDesktop.Models.Fits;

namespace CanfarDesktop.Services.Fits;

/// <summary>
/// The image a mark export describes, read from the file itself when it is not the one on screen: the
/// size and sky coordinates of the extension the marks are on, from its header alone — not a pixel is
/// read. The viewer answers for the file it shows; this for any other, which export_annotations used to
/// refuse although every other mark tool takes a file that is not open (QA D5b).
/// </summary>
public static class MarkExportFiles
{
    /// <summary>
    /// The source for the marks kept under <paramref name="target"/>: its extension, or for a bare path the
    /// file's first image, where such marks land when it is opened. Null, with why, when there is none.
    /// </summary>
    public static (MarkExport.Source? Source, string? Why) Read(string target)
    {
        var (path, hdu) = MarkTarget.Parse(target);
        try
        {
            using var stream = FitsContainer.OpenFits(path);
            var images = FitsLayout.Read(stream)
                .Where(h => FitsImageEncodings.For(h.Header) is not null)
                .ToList();

            var chosen = hdu is { } index ? images.FirstOrDefault(h => h.Index == index) : images.FirstOrDefault();
            if (chosen is null)
                return (null, hdu is { } missing ? $"the file has no image at extension {missing}" : "the file holds no image");

            // A compressed image's own header describes the image, not the table it is stored in.
            var header = FitsImageEncodings.For(chosen.Header)!.ImageCards(chosen).Parse();
            return (new MarkExport.Source(
                LocalPath: path,
                FileName: Path.GetFileName(path),
                HduIndex: chosen.Index,
                HduName: chosen.Header.GetString("EXTNAME"),
                Width: header.NAxis1,
                Height: header.NAxis2,
                Wcs: WcsInfo.FromHeader(header)), null);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
                                       or NotSupportedException or ArgumentException)
        {
            return (null, $"the file cannot be read as FITS: {ex.Message}");
        }
    }
}
