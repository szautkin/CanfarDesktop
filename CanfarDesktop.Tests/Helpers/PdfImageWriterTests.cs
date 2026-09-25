using System.Text;
using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// The figure exports' PDF writer, which had no tests of its own.
///
/// <para>The one that matters most: a PDF here carries no alpha, so a figure with a transparent
/// background has to be flattened onto paper first. Two of the three export paths once skipped that
/// and wrote the premultiplied pixels straight through — and a transparent pixel is (0,0,0,0), so the
/// page came out black with the light theme's dark text on it.</para>
/// </summary>
public class PdfImageWriterTests
{
    /// <summary>One premultiplied BGRA pixel in, one RGB pixel out.</summary>
    private static byte[] OverWhite(byte b, byte g, byte r, byte a)
        => PdfImageWriter.BgraToRgbOverWhite([b, g, r, a], 1, 1);

    [Fact]
    public void ATransparentPixelLandsOnWhitePaper_NotBlack()
        => Assert.Equal(new byte[] { 255, 255, 255 }, OverWhite(0, 0, 0, 0));

    [Fact]
    public void AnOpaquePixelIsUnchanged_SoFlatteningEveryFigureIsSafe()
        => Assert.Equal(new byte[] { 200, 100, 50 }, OverWhite(b: 50, g: 100, r: 200, a: 255));

    [Fact]
    public void AHalfCoveredPixelIsBlendedWithThePaper()
        // Premultiplied red at half cover over white: out = src + 255 * (1 - a).
        => Assert.Equal(new byte[] { 255, 127, 127 }, OverWhite(b: 0, g: 0, r: 128, a: 128));

    [Fact]
    public void WritesAWholePdfDocument()
    {
        using var stream = new MemoryStream();
        PdfImageWriter.Write(stream, PdfImageWriter.BgraToRgbOverWhite(new byte[2 * 2 * 4], 2, 2), 2, 2);

        var text = Encoding.ASCII.GetString(stream.ToArray());
        Assert.StartsWith("%PDF-", text);
        Assert.Contains("/FlateDecode", text);
        Assert.EndsWith("%%EOF", text.TrimEnd());
    }
}
