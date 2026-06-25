using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Xunit;
using CanfarDesktop.Services.Fits;

namespace CanfarDesktop.Tests.Services.Fits;

public class FitsContainerTests
{
    /// <summary>A minimal valid FITS file: a 2x2 BITPIX=8 image (one header block + one data block).</summary>
    private static byte[] MinimalFits()
    {
        string Card(string kw, string val) => $"{kw,-8}= {val,-20}".PadRight(80)[..80];
        var cards = string.Concat(
            Card("SIMPLE", "T"),
            Card("BITPIX", "8"),
            Card("NAXIS", "2"),
            Card("NAXIS1", "2"),
            Card("NAXIS2", "2"),
            "END".PadRight(80));
        var header = Encoding.ASCII.GetBytes(cards.PadRight(2880));
        var data = new byte[2880];
        data[0] = 10; data[1] = 20; data[2] = 30; data[3] = 40;
        return [.. header, .. data];
    }

    private static byte[] Tar(params (string Name, byte[] Content)[] entries)
    {
        var ms = new MemoryStream();
        using (var writer = new TarWriter(ms, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(content) };
                writer.WriteEntry(entry);
            }
        }
        return ms.ToArray();
    }

    private static byte[] Gzip(byte[] content)
    {
        var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionMode.Compress, leaveOpen: true))
            gz.Write(content);
        return ms.ToArray();
    }

    private static byte[] ReadAll(Stream s)
    {
        s.Position = 0;
        var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    [Fact]
    public void PlainFits_PassesThrough_Unchanged()
    {
        var fits = MinimalFits();
        using var result = FitsContainer.Unwrap(new MemoryStream(fits), 0);
        Assert.Equal(fits, ReadAll(result));

        // And the parser is happy with it.
        result.Position = 0;
        var headers = FitsParser.ParseHeaders(result);
        Assert.Single(headers);
        Assert.Equal(2, headers[0].NAxis);
    }

    [Fact]
    public void TarBundle_ExtractsFitsMember()
    {
        // Mirrors CADC's HST "download all" packaging: HST/product/xxx_flt.fits inside a tar.
        var tar = Tar(("HST/product/ibf404jwq_flt.fits", MinimalFits()));
        using var result = FitsContainer.Unwrap(new MemoryStream(tar), 0);

        var headers = FitsParser.ParseHeaders(result);
        Assert.Single(headers);
        Assert.Equal(2, headers[0].NAxis);
    }

    [Fact]
    public void Tar_PicksFitsMember_SkippingNonFitsEntries()
    {
        var tar = Tar(
            ("readme.txt", Encoding.ASCII.GetBytes("not fits")),
            ("HST/product/x_flt.fits", MinimalFits()));
        using var result = FitsContainer.Unwrap(new MemoryStream(tar), 0);
        Assert.Equal(MinimalFits(), ReadAll(result));
    }

    [Fact]
    public void Tar_NoFitsMember_ThrowsActionable()
    {
        var tar = Tar(("readme.txt", Encoding.ASCII.GetBytes("just a note")));
        var ex = Assert.Throws<InvalidDataException>(() => FitsContainer.Unwrap(new MemoryStream(tar), 0));
        Assert.Contains("no .fits file", ex.Message);
    }

    [Fact]
    public void GzippedFits_IsDecompressed()
    {
        using var result = FitsContainer.Unwrap(new MemoryStream(Gzip(MinimalFits())), 0);
        Assert.Equal(MinimalFits(), ReadAll(result));
    }

    [Fact]
    public void GzippedTar_IsDecompressedThenUntarred()
    {
        var gztar = Gzip(Tar(("x.fits", MinimalFits())));
        using var result = FitsContainer.Unwrap(new MemoryStream(gztar), 0);
        Assert.Equal(MinimalFits(), ReadAll(result));
    }

    [Fact]
    public void HtmlErrorBody_ThrowsWithHtmlHint()
    {
        var html = Encoding.ASCII.GetBytes("<html><body>403 Forbidden</body></html>");
        var ex = Assert.Throws<InvalidDataException>(() => FitsContainer.Unwrap(new MemoryStream(html), 0));
        Assert.Contains("not a FITS image", ex.Message);
        Assert.Contains("HTML", ex.Message);
    }

    [Fact]
    public void LooksLikeTar_TrueForTarHeader_FalseForFits()
    {
        Assert.True(FitsContainer.LooksLikeTar(Tar(("x.fits", MinimalFits()))));
        Assert.False(FitsContainer.LooksLikeTar(MinimalFits()));
    }

    [Fact]
    public async Task OpenFits_FromTempFile_UnwrapsTar()
    {
        var path = Path.Combine(Path.GetTempPath(), "fitsctr-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllBytesAsync(path, Tar(("HST/product/x_flt.fits", MinimalFits())));
        try
        {
            using var result = FitsContainer.OpenFits(path);
            Assert.Equal(MinimalFits(), ReadAll(result));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
