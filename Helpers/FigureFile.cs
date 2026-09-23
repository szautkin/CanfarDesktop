using Windows.Graphics.Imaging;

namespace CanfarDesktop.Helpers;

/// <summary>
/// A rasterised figure written to disk as PNG or PDF.
///
/// <para>This was written three times — the FITS figure export, the cube's MCP export and the cube's
/// export dialog — and the copies had already drifted: only the dialog flattened a transparent figure
/// onto white before handing it to the PDF writer. The other two passed premultiplied pixels straight
/// through, and a transparent pixel is (0,0,0,0), so a transparent-background PDF came out on a black
/// page. PDF carries no alpha here, so flattening is right for EVERY figure — an opaque pixel comes
/// through it unchanged.</para>
///
/// <para>Written atomically, like the other files the app produces, so a failed export leaves whatever
/// was at the path before rather than half a figure.</para>
/// </summary>
public static class FigureFile
{
    /// <summary>
    /// Write premultiplied BGRA8 pixels to <paramref name="path"/>: a PDF when <paramref name="pdf"/>,
    /// otherwise a PNG that keeps its alpha.
    /// </summary>
    public static Task WriteAsync(string path, byte[] bgra, int width, int height, bool pdf)
        => AtomicFile.WriteStreamAsync(path, async stream =>
        {
            if (pdf)
            {
                PdfImageWriter.Write(stream, PdfImageWriter.BgraToRgbOverWhite(bgra, width, height), width, height);
                return;
            }

            using var random = stream.AsRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, random);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                (uint)width, (uint)height, 96, 96, bgra);
            await encoder.FlushAsync();
        });
}
