using System.IO;
using System.IO.Compression;
using System.Text;

namespace CanfarDesktop.Services.CubeViewer;

/// <summary>
/// Writes a minimal, dependency-free single-page PDF that embeds one RGB raster (the export
/// figure plate) losslessly via FlateDecode. The page is sized 1:1 to the image in points.
/// Used so the cube viewer can export a publication figure as PDF as well as PNG.
/// </summary>
internal static class PdfImageWriter
{
    /// <summary>
    /// Write a PDF embedding <paramref name="rgb"/> (tight width·height·3, top-down) to the stream.
    /// </summary>
    public static void Write(Stream output, byte[] rgb, int width, int height)
    {
        byte[] compressed = Deflate(rgb);

        var offsets = new List<long>();
        long pos = 0;
        var ms = new MemoryStream();

        void WriteAscii(string s)
        {
            var b = Encoding.ASCII.GetBytes(s);
            ms.Write(b, 0, b.Length);
            pos += b.Length;
        }
        void WriteBytes(byte[] b)
        {
            ms.Write(b, 0, b.Length);
            pos += b.Length;
        }
        void StartObj() => offsets.Add(pos);

        WriteAscii("%PDF-1.7\n%âãÏÓ\n");

        // 1: Catalog
        StartObj();
        WriteAscii("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        // 2: Pages
        StartObj();
        WriteAscii("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        // 3: Page (MediaBox in points = image pixels at 72 dpi)
        StartObj();
        WriteAscii($"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}] " +
                   "/Resources << /XObject << /Im0 5 0 R >> >> /Contents 4 0 R >>\nendobj\n");

        // 4: Content stream — scale the unit image to the full page.
        string content = $"q\n{width} 0 0 {height} 0 0 cm\n/Im0 Do\nQ\n";
        StartObj();
        WriteAscii($"4 0 obj\n<< /Length {content.Length} >>\nstream\n");
        WriteAscii(content);
        WriteAscii("endstream\nendobj\n");

        // 5: Image XObject (DeviceRGB, 8 bpc, FlateDecode).
        StartObj();
        WriteAscii($"5 0 obj\n<< /Type /XObject /Subtype /Image /Width {width} /Height {height} " +
                   $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /Length {compressed.Length} >>\nstream\n");
        WriteBytes(compressed);
        WriteAscii("\nendstream\nendobj\n");

        // xref
        long xrefPos = pos;
        int count = offsets.Count + 1; // + free object 0
        WriteAscii($"xref\n0 {count}\n");
        WriteAscii("0000000000 65535 f \n");
        foreach (var off in offsets)
            WriteAscii(off.ToString("D10") + " 00000 n \n");

        WriteAscii($"trailer\n<< /Size {count} /Root 1 0 R >>\nstartxref\n{xrefPos}\n%%EOF\n");

        ms.Position = 0;
        ms.CopyTo(output);
    }

    /// <summary>Convert tight BGRA8 (top-down) to tight RGB8 (top-down), dropping alpha.</summary>
    public static byte[] BgraToRgb(byte[] bgra, int width, int height)
    {
        var rgb = new byte[(long)width * height * 3];
        long n = (long)width * height;
        for (long i = 0; i < n; i++)
        {
            long s = i * 4, d = i * 3;
            rgb[d + 0] = bgra[s + 2]; // R
            rgb[d + 1] = bgra[s + 1]; // G
            rgb[d + 2] = bgra[s + 0]; // B
        }
        return rgb;
    }

    private static byte[] Deflate(byte[] data)
    {
        using var ms = new MemoryStream();
        // PDF FlateDecode expects a zlib (RFC 1950) stream — ZLibStream emits exactly that.
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(data, 0, data.Length);
        return ms.ToArray();
    }
}
