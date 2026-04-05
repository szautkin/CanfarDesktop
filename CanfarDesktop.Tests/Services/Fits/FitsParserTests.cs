using Xunit;
using CanfarDesktop.Models.Fits;
using CanfarDesktop.Services.Fits;

namespace CanfarDesktop.Tests.Services.Fits;

public class FitsParserTests
{
    /// <summary>Build a minimal valid FITS file in memory for testing.</summary>
    private static MemoryStream BuildFitsStream(int bitpix, int width, int height, byte[]? data = null)
    {
        var ms = new MemoryStream();
        var header = new List<string>
        {
            FormatCard("SIMPLE", "T"),
            FormatCard("BITPIX", bitpix.ToString()),
            FormatCard("NAXIS", "2"),
            FormatCard("NAXIS1", width.ToString()),
            FormatCard("NAXIS2", height.ToString()),
            "END".PadRight(80),
        };

        // Write header block(s) — pad to 2880 bytes
        var headerBytes = string.Join("", header.Select(c => c.PadRight(80)[..80]));
        var headerBlock = System.Text.Encoding.ASCII.GetBytes(headerBytes.PadRight(2880));
        ms.Write(headerBlock);

        // Write data block
        if (data is not null)
        {
            ms.Write(data);
            // Pad to 2880-byte boundary
            var remainder = data.Length % 2880;
            if (remainder > 0) ms.Write(new byte[2880 - remainder]);
        }

        ms.Position = 0;
        return ms;
    }

    private static string FormatCard(string keyword, string value) =>
        $"{keyword,-8}= {value,-20}".PadRight(80)[..80];

    [Fact]
    public void ParseCard_SimpleKeyword()
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes("BITPIX  =                   16 / bits per pixel                                ");
        var card = FitsParser.ParseCard(bytes);

        Assert.Equal("BITPIX", card.Keyword);
        Assert.Equal("16", card.Value);
        Assert.Contains("bits per pixel", card.Comment);
    }

    [Fact]
    public void ParseCard_StringValue()
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes("CTYPE1  = 'RA---TAN'           / WCS projection                                ");
        var card = FitsParser.ParseCard(bytes);

        Assert.Equal("CTYPE1", card.Keyword);
        Assert.Equal("RA---TAN", card.Value);
    }

    [Fact]
    public void ParseCard_EndKeyword()
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes("END" + new string(' ', 77));
        var card = FitsParser.ParseCard(bytes);

        Assert.Equal("END", card.Keyword);
    }

    [Fact]
    public void ReadHeader_ParsesAllKeywords()
    {
        using var stream = BuildFitsStream(16, 100, 100);
        var header = FitsParser.ReadHeader(stream);

        Assert.NotNull(header);
        Assert.Equal(16, header!.BitPix);
        Assert.Equal(2, header.NAxis);
        Assert.Equal(100, header.NAxis1);
        Assert.Equal(100, header.NAxis2);
    }

    [Fact]
    public void Parse_8BitImage_ReadsPixels()
    {
        var width = 4;
        var height = 3;
        var data = new byte[width * height];
        for (var i = 0; i < data.Length; i++) data[i] = (byte)(i * 20);

        using var stream = BuildFitsStream(8, width, height, data);
        var hdus = FitsParser.Parse(stream);

        Assert.Single(hdus);
        Assert.NotNull(hdus[0].ImageData);
        Assert.Equal(width, hdus[0].ImageData!.Width);
        Assert.Equal(height, hdus[0].ImageData!.Height);
        Assert.Equal(width * height, hdus[0].ImageData!.Pixels.Length);
    }

    [Fact]
    public void Parse_16BitImage_BigEndian()
    {
        var width = 2;
        var height = 2;
        // Big-endian 16-bit: [0x00, 0x0A] = 10, [0x00, 0x14] = 20, etc.
        var data = new byte[] { 0, 10, 0, 20, 0, 30, 0, 40 };

        using var stream = BuildFitsStream(16, width, height, data);
        var hdus = FitsParser.Parse(stream);

        var pixels = hdus[0].ImageData!.Pixels;
        Assert.Equal(10f, pixels[0]);
        Assert.Equal(20f, pixels[1]);
        Assert.Equal(30f, pixels[2]);
        Assert.Equal(40f, pixels[3]);
    }

    [Fact]
    public void FitsHeader_TypedAccessors()
    {
        var header = new FitsHeader();
        header.Add(new FitsCard("NAXIS1", "2048", ""));
        header.Add(new FitsCard("BSCALE", "1.5", ""));
        header.Add(new FitsCard("SIMPLE", "T", ""));

        Assert.Equal(2048, header.GetInt("NAXIS1"));
        Assert.Equal(1.5, header.GetDouble("BSCALE"));
        Assert.True(header.GetBool("SIMPLE"));
        Assert.Equal(0, header.GetInt("MISSING"));
    }

    [Fact]
    public void WcsInfo_PixelToWorld()
    {
        var wcs = new WcsInfo
        {
            CrPix1 = 512, CrPix2 = 512,
            CrVal1 = 180.0, CrVal2 = 45.0,
            Cd1_1 = -0.001, Cd1_2 = 0,
            Cd2_1 = 0, Cd2_2 = 0.001,
        };

        var (ra, dec) = wcs.PixelToWorld(512, 512);
        Assert.Equal(180.0, ra, 6);
        Assert.Equal(45.0, dec, 6);

        var (ra2, dec2) = wcs.PixelToWorld(612, 612);
        Assert.True(ra2 < 180.0); // negative CD1_1 = RA decreases with x
        Assert.True(dec2 > 45.0); // positive CD2_2 = Dec increases with y
    }

    [Fact]
    public void WcsInfo_FormatRa()
    {
        // 180 degrees = 12h 00m 00.00s
        var result = WcsInfo.FormatRa(180.0);
        Assert.StartsWith("12h00m", result);
    }

    [Fact]
    public void WcsInfo_FormatDec()
    {
        var result = WcsInfo.FormatDec(45.5);
        Assert.StartsWith("+45", result);
    }

    [Fact]
    public void WcsInfo_FromHeader_CdMatrix()
    {
        var header = new FitsHeader();
        header.Add(new FitsCard("CRPIX1", "512", ""));
        header.Add(new FitsCard("CRPIX2", "512", ""));
        header.Add(new FitsCard("CRVAL1", "180.0", ""));
        header.Add(new FitsCard("CRVAL2", "45.0", ""));
        header.Add(new FitsCard("CD1_1", "-0.001", ""));
        header.Add(new FitsCard("CD1_2", "0", ""));
        header.Add(new FitsCard("CD2_1", "0", ""));
        header.Add(new FitsCard("CD2_2", "0.001", ""));

        var wcs = WcsInfo.FromHeader(header);
        Assert.Equal(-0.001, wcs.Cd1_1, 6);
        Assert.True(wcs.IsValid);
    }

    [Fact]
    public void WcsInfo_FromHeader_CdeltFallback()
    {
        var header = new FitsHeader();
        header.Add(new FitsCard("CRPIX1", "100", ""));
        header.Add(new FitsCard("CRPIX2", "100", ""));
        header.Add(new FitsCard("CRVAL1", "90.0", ""));
        header.Add(new FitsCard("CRVAL2", "30.0", ""));
        header.Add(new FitsCard("CDELT1", "-0.001", ""));
        header.Add(new FitsCard("CDELT2", "0.001", ""));

        var wcs = WcsInfo.FromHeader(header);
        Assert.True(wcs.IsValid);
        Assert.Equal(-0.001, wcs.Cd1_1, 6);
    }
}
