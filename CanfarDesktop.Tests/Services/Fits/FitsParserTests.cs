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

    /// <summary>Write one 2880-byte header block from the given cards (END is appended).</summary>
    private static void WriteHeaderBlock(Stream ms, params string[] cards)
    {
        var all = cards.Append("END".PadRight(80));
        var headerBytes = string.Join("", all.Select(c => c.PadRight(80)[..80]));
        ms.Write(System.Text.Encoding.ASCII.GetBytes(headerBytes.PadRight(2880)));
    }

    /// <summary>Build an fpack/Rice file: empty primary HDU + a compressed BINTABLE image extension
    /// (ZIMAGE=T, ZCMPTYPE='RICE_1'). This is exactly the shape a real `.fits.fz` has.</summary>
    private static MemoryStream BuildFpackStream(bool withPlainPrimaryImage = false)
    {
        var ms = new MemoryStream();
        if (withPlainPrimaryImage)
        {
            // A renderable primary image — the parser should still open this and NOT fail.
            WriteHeaderBlock(ms, FormatCard("SIMPLE", "T"), FormatCard("BITPIX", "8"),
                FormatCard("NAXIS", "2"), FormatCard("NAXIS1", "4"), FormatCard("NAXIS2", "3"),
                FormatCard("EXTEND", "T"));
            ms.Write(new byte[2880]); // 12 pixel bytes + padding
        }
        else
        {
            // The fpack hallmark: an empty primary HDU (NAXIS=0).
            WriteHeaderBlock(ms, FormatCard("SIMPLE", "T"), FormatCard("BITPIX", "8"),
                FormatCard("NAXIS", "0"), FormatCard("EXTEND", "T"));
        }
        // Rice-compressed image carried in a binary-table extension.
        WriteHeaderBlock(ms, FormatCard("XTENSION", "'BINTABLE'"), FormatCard("BITPIX", "8"),
            FormatCard("NAXIS", "2"), FormatCard("NAXIS1", "8"), FormatCard("NAXIS2", "4"),
            FormatCard("PCOUNT", "0"), FormatCard("GCOUNT", "1"),
            FormatCard("ZIMAGE", "T"), FormatCard("ZCMPTYPE", "'RICE_1'"));
        ms.Write(new byte[2880]); // the BINTABLE's data block
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void Parse_FpackCompressed_ThrowsActionableError()
    {
        // SCI-11: a `.fits.fz` (empty primary + Rice-compressed BINTABLE) must fail fast with a
        // clear "run funpack" message — not silently render the compressed table as a garbage image.
        using var stream = BuildFpackStream();
        var ex = Assert.Throws<InvalidDataException>(() => FitsParser.Parse(stream));
        Assert.Contains("funpack", ex.Message);
        Assert.Contains("RICE_1", ex.Message);
    }

    [Fact]
    public void Parse_PlainImageWithCompressedExtension_StillOpens()
    {
        // Guard: a real primary image alongside a compressed extension still opens (no false positive).
        using var stream = BuildFpackStream(withPlainPrimaryImage: true);
        var hdus = FitsParser.Parse(stream);
        Assert.NotNull(hdus[0].ImageData);
        Assert.Equal(4, hdus[0].ImageData!.Width);
    }

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
    public void WcsInfo_FormatRa_ExactHours()
    {
        // 180 degrees = 12h 00m 00.00s
        Assert.Equal("12h00m00.00s", WcsInfo.FormatRa(180.0));
    }

    [Fact]
    public void WcsInfo_FormatRa_Zero()
    {
        Assert.Equal("00h00m00.00s", WcsInfo.FormatRa(0.0));
    }

    [Theory]
    [InlineData(19.8, "01h19m")]    // CADC test: 19.8° = 1.32h
    [InlineData(83.633, "05h34m")]  // Near Crab Nebula
    [InlineData(270.0, "18h00m")]   // Exact hours
    public void WcsInfo_FormatRa_StartsCorrectly(double raDeg, string expectedStart)
    {
        var result = WcsInfo.FormatRa(raDeg);
        Assert.StartsWith(expectedStart, result);
        Assert.EndsWith("s", result);
    }

    [Fact]
    public void WcsInfo_FormatDec_PositiveExact()
    {
        Assert.StartsWith("+45\u00b030'00", WcsInfo.FormatDec(45.5));
    }

    [Fact]
    public void WcsInfo_FormatDec_Negative()
    {
        var result = WcsInfo.FormatDec(-33.5);
        Assert.StartsWith("-33\u00b030'", result);
    }

    [Fact]
    public void WcsInfo_FormatDec_Zero()
    {
        var result = WcsInfo.FormatDec(0.0);
        Assert.StartsWith("+00\u00b000'", result);
    }

    [Fact]
    public void WcsInfo_FormatDec_CadcExample()
    {
        // CADC returned Dec: 42.10111111 → +42°06'04.0"
        var result = WcsInfo.FormatDec(42.10111111);
        Assert.StartsWith("+42\u00b006'", result);
    }

    [Fact]
    public void WcsInfo_FormatForResolver_CadcExample()
    {
        // CADC test case: RA=19.8°, Dec=42.10111111°
        // RA: 19.8/15 = 1.32h → 01h19m12.00s → rsInt=1200
        // Dec: 42°06'04.0" → dsInt=40
        var result = WcsInfo.FormatForResolver(19.8, 42.10111111);
        Assert.Equal("01:19:1200,+42:06:040", result);
    }

    [Fact]
    public void WcsInfo_FormatForResolver_ExactValues()
    {
        // 180° RA = 12h exactly, 45° Dec exactly
        var result = WcsInfo.FormatForResolver(180.0, 45.0);
        Assert.Equal("12:00:0000,+45:00:000", result);
    }

    [Fact]
    public void WcsInfo_FormatForResolver_NegativeDec()
    {
        var result = WcsInfo.FormatForResolver(270.0, -33.5);
        Assert.Equal("18:00:0000,-33:30:000", result);
    }

    [Fact]
    public void WcsInfo_FormatForResolver_NoSpaces()
    {
        // CADC resolver requires no spaces in the coordinate string
        var result = WcsInfo.FormatForResolver(19.8, 42.10111111);
        Assert.DoesNotContain(" ", result);
    }

    [Fact]
    public void WcsInfo_FormatForResolver_NoLetterF()
    {
        // Regression: C# format "05.2f" outputs literal 'f'. Ensure no 'f' in output.
        var result = WcsInfo.FormatForResolver(83.633, 22.0145);
        Assert.DoesNotContain("f", result);
    }

    [Fact]
    public void WcsInfo_FormatRa_NoLetterF()
    {
        // Regression: ensure format strings don't produce literal 'f'
        var result = WcsInfo.FormatRa(83.633);
        Assert.DoesNotContain("f", result);
    }

    [Fact]
    public void WcsInfo_FormatDec_NoLetterF()
    {
        var result = WcsInfo.FormatDec(22.0145);
        Assert.DoesNotContain("f", result);
    }

    [Fact]
    public void WcsInfo_WorldToPixel_Roundtrip()
    {
        var wcs = new WcsInfo
        {
            CrPix1 = 512, CrPix2 = 512,
            CrVal1 = 180.0, CrVal2 = 45.0,
            Cd1_1 = -0.001, Cd1_2 = 0,
            Cd2_1 = 0, Cd2_2 = 0.001,
        };

        // Forward: pixel → world
        var (ra, dec) = wcs.PixelToWorld(612, 712);
        // Inverse: world → pixel
        var pixel = wcs.WorldToPixel(ra, dec);

        Assert.NotNull(pixel);
        Assert.Equal(612.0, pixel.Value.Px, 6);
        Assert.Equal(712.0, pixel.Value.Py, 6);
    }

    [Fact]
    public void WcsInfo_WorldToPixel_Roundtrip_Rotated()
    {
        // CD matrix with 30° rotation
        var angle = 30.0 * Math.PI / 180.0;
        var scale = 0.001;
        var wcs = new WcsInfo
        {
            CrPix1 = 256, CrPix2 = 256,
            CrVal1 = 90.0, CrVal2 = -30.0,
            Cd1_1 = -scale * Math.Cos(angle),
            Cd1_2 = scale * Math.Sin(angle),
            Cd2_1 = scale * Math.Sin(angle),
            Cd2_2 = scale * Math.Cos(angle),
        };

        var (ra, dec) = wcs.PixelToWorld(300, 400);
        var pixel = wcs.WorldToPixel(ra, dec);

        Assert.NotNull(pixel);
        Assert.Equal(300.0, pixel.Value.Px, 6);
        Assert.Equal(400.0, pixel.Value.Py, 6);
    }

    [Fact]
    public void WcsInfo_WorldToPixel_AtReferencePixel()
    {
        var wcs = new WcsInfo
        {
            CrPix1 = 512, CrPix2 = 512,
            CrVal1 = 180.0, CrVal2 = 45.0,
            Cd1_1 = -0.001, Cd1_2 = 0,
            Cd2_1 = 0, Cd2_2 = 0.001,
        };

        // CrVal should map back to CrPix
        var pixel = wcs.WorldToPixel(180.0, 45.0);
        Assert.NotNull(pixel);
        Assert.Equal(512.0, pixel.Value.Px, 6);
        Assert.Equal(512.0, pixel.Value.Py, 6);
    }

    [Fact]
    public void WcsInfo_WorldToPixel_SingularMatrix_ReturnsNull()
    {
        var wcs = new WcsInfo
        {
            CrPix1 = 100, CrPix2 = 100,
            CrVal1 = 0, CrVal2 = 0,
            Cd1_1 = 0, Cd1_2 = 0, // all zeros → singular
            Cd2_1 = 0, Cd2_2 = 0,
        };

        Assert.Null(wcs.WorldToPixel(10.0, 20.0));
    }

    [Fact]
    public void WcsInfo_NorthAngle_NoRotation()
    {
        // Standard orientation: North up, no rotation
        var wcs = new WcsInfo
        {
            CrPix1 = 512, CrPix2 = 512,
            CrVal1 = 180.0, CrVal2 = 45.0,
            Cd1_1 = -0.001, Cd1_2 = 0,
            Cd2_1 = 0, Cd2_2 = 0.001,
        };
        Assert.Equal(0.0, wcs.NorthAngle, 1);
        Assert.False(wcs.HasParityFlip);
    }

    [Fact]
    public void WcsInfo_NorthAngle_Rotated45()
    {
        var angle = 45.0 * Math.PI / 180.0;
        var wcs = new WcsInfo
        {
            CrPix1 = 256, CrPix2 = 256,
            CrVal1 = 90.0, CrVal2 = 30.0,
            Cd1_1 = -0.001 * Math.Cos(angle),
            Cd1_2 = 0.001 * Math.Sin(angle),
            Cd2_1 = 0.001 * Math.Sin(angle),
            Cd2_2 = 0.001 * Math.Cos(angle),
        };
        Assert.Equal(-45.0, wcs.NorthAngle, 1);
    }

    [Fact]
    public void WcsInfo_ParityFlip_Detected()
    {
        // Positive determinant = parity flip (East right instead of left)
        var wcs = new WcsInfo
        {
            Cd1_1 = 0.001, Cd1_2 = 0,
            Cd2_1 = 0, Cd2_2 = 0.001,
        };
        Assert.True(wcs.HasParityFlip);
    }

    [Fact]
    public void WcsInfo_PixelScale()
    {
        var wcs = new WcsInfo
        {
            Cd1_1 = -0.001, Cd1_2 = 0,
            Cd2_1 = 0, Cd2_2 = 0.001,
        };
        // 0.001 deg/px = 3.6 arcsec/px
        Assert.Equal(3.6, wcs.PixelScaleArcsec, 1);
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

    /// <summary>
    /// Regression for the CFHT .fits.fz failure: FITS data size is |BITPIX|/8 x GCOUNT x
    /// (PCOUNT + NAXIS product). An fpack BINTABLE keeps the compressed image in the PCOUNT
    /// heap; omitting it made the HDU skip land mid-heap, so the parser read compressed bytes
    /// as a "header" and died with "FITS header exceeds maximum allowed size".
    /// </summary>
    [Fact]
    public void Parse_MultiPlaneCube_SkipsAllPlanes_ThenReadsTrailingHdu()
    {
        // Regression (WFPC2 c0f, w1lm060bt): a NAXIS=3 cube followed by another HDU. The 2D viewer
        // reads only plane 1, but the parser must skip ALL planes so the trailing HDU is found —
        // otherwise planes 2..N are misread as a header and blow the max-header-size guard.
        var ms = new MemoryStream();
        const int w = 4, h = 3, planes = 4;
        WriteHeaderBlock(ms,
            FormatCard("SIMPLE", "T"), FormatCard("BITPIX", "16"),
            FormatCard("NAXIS", "3"), FormatCard("NAXIS1", w.ToString()),
            FormatCard("NAXIS2", h.ToString()), FormatCard("NAXIS3", planes.ToString()));
        // Cube data: w*h*planes int16, block-padded.
        var cubeBytes = ((w * h * planes * 2 + 2879) / 2880) * 2880;
        ms.Write(new byte[cubeBytes]);
        // A trailing IMAGE extension only reachable if the whole cube was skipped.
        WriteHeaderBlock(ms,
            FormatCard("XTENSION", "'IMAGE   '"), FormatCard("BITPIX", "16"),
            FormatCard("NAXIS", "2"), FormatCard("NAXIS1", "5"), FormatCard("NAXIS2", "2"),
            FormatCard("PCOUNT", "0"), FormatCard("GCOUNT", "1"));
        ms.Write(new byte[2880]);
        ms.Position = 0;

        var hdus = FitsParser.Parse(ms);
        // Primary cube read as plane 1 (NAXIS1=4) + the trailing 5x2 extension both present.
        Assert.Contains(hdus, hd => hd.Header.NAxis == 3 && hd.ImageData is not null);
        Assert.Contains(hdus, hd => hd.Header.NAxis1 == 5 && hd.Header.NAxis2 == 2);
    }

    [Fact]
    public void Parse_RealWfpc2Cube_WhenAvailable()
    {
        const string path = @"C:\Users\szaut\OneDrive\Documents\w1lm060bt_c0f.fits";
        if (!File.Exists(path)) return;
        using var stream = File.OpenRead(path);
        var hdus = FitsParser.Parse(stream); // must not throw "header exceeds maximum size"
        Assert.Contains(hdus, hd => hd.ImageData is { Width: 800, Height: 800 });
    }

    [Fact]
    public void Parse_FpackBintableWithHeap_SkipsHeapAndReportsFunpackError()
    {
        var ms = new MemoryStream();
        // Data-less primary.
        WriteHeaderBlock(ms,
            FormatCard("SIMPLE", "T"), FormatCard("BITPIX", "16"),
            FormatCard("NAXIS", "0"), FormatCard("EXTEND", "T"));
        // fpack-style BINTABLE: tiny table (8x4 bytes) + a heap much larger than the table.
        const int tableBytes = 8 * 4;
        const int heapBytes = 3 * 2880 + 123; // deliberately not block-aligned
        WriteHeaderBlock(ms,
            FormatCard("XTENSION", "'BINTABLE'"), FormatCard("BITPIX", "8"),
            FormatCard("NAXIS", "2"), FormatCard("NAXIS1", "8"), FormatCard("NAXIS2", "4"),
            FormatCard("PCOUNT", heapBytes.ToString()), FormatCard("GCOUNT", "1"),
            FormatCard("TFIELDS", "1"), FormatCard("ZIMAGE", "T"),
            FormatCard("ZCMPTYPE", "'RICE_1  '"));
        var dataAndHeap = new byte[((tableBytes + heapBytes + 2879) / 2880) * 2880];
        ms.Write(dataAndHeap);
        ms.Position = 0;

        // With PCOUNT honored the parser skips the whole heap cleanly and reaches the intended,
        // actionable fpack error - NOT the header-size crash.
        var ex = Assert.Throws<InvalidDataException>(() => FitsParser.Parse(ms));
        Assert.Contains("funpack", ex.Message);
        Assert.DoesNotContain("maximum allowed size", ex.Message);
    }

    [Fact]
    public void Parse_FpackBintable_FollowedByPlainImage_ReadsTheImage()
    {
        var ms = new MemoryStream();
        WriteHeaderBlock(ms,
            FormatCard("SIMPLE", "T"), FormatCard("BITPIX", "16"),
            FormatCard("NAXIS", "0"), FormatCard("EXTEND", "T"));
        const int heapBytes = 2880 + 7;
        WriteHeaderBlock(ms,
            FormatCard("XTENSION", "'BINTABLE'"), FormatCard("BITPIX", "8"),
            FormatCard("NAXIS", "2"), FormatCard("NAXIS1", "8"), FormatCard("NAXIS2", "4"),
            FormatCard("PCOUNT", heapBytes.ToString()), FormatCard("GCOUNT", "1"),
            FormatCard("ZIMAGE", "T"));
        ms.Write(new byte[((8 * 4 + heapBytes + 2879) / 2880) * 2880]);
        // A plain 4x2 int16 image HDU AFTER the compressed one — only reachable with a correct skip.
        WriteHeaderBlock(ms,
            FormatCard("XTENSION", "'IMAGE   '"), FormatCard("BITPIX", "16"),
            FormatCard("NAXIS", "2"), FormatCard("NAXIS1", "4"), FormatCard("NAXIS2", "2"),
            FormatCard("PCOUNT", "0"), FormatCard("GCOUNT", "1"));
        ms.Write(new byte[2880]);
        ms.Position = 0;

        var hdus = FitsParser.Parse(ms);
        Assert.Contains(hdus, h => h.ImageData is not null && h.Header.NAxis1 == 4);
    }
}
