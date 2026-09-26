using Xunit;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Models.Cutouts;
using CanfarDesktop.Models.Fits;
using CanfarDesktop.Services.Cutouts;
using CanfarDesktop.Services.Cutouts.Local;
using CanfarDesktop.Services.Fits;
using static CanfarDesktop.Tests.Helpers.SyntheticFits;

namespace CanfarDesktop.Tests.Services.Cutouts;

/// <summary>
/// A cut made on this computer, from a file already downloaded. Every file here is synthetic, so the
/// test knows every byte of it: a cut must keep the pixels it copies exactly as they were, and a star
/// must be at the same place on the sky before and after.
/// </summary>
public class LocalCutoutTests : IDisposable
{
    private const double Scale = 1e-4; // degrees per pixel: 0.36″
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "verbinal-localcut-" + Guid.NewGuid().ToString("N")[..8]);

    public LocalCutoutTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    // ── Making and reading cuts ──────────────────────────────────────────────

    private static LocalCutoutSource Source(string path) => new(LocalFitsFile.Inspect(path, "cadc:TEST/" + Path.GetFileName(path)));

    /// <summary>A circle of <paramref name="radiusPixels"/> about a pixel of the file's first image.</summary>
    private static CutoutSpec CircleAt(LocalCutoutSource source, double x, double y, double radiusPixels)
    {
        var (ra, dec) = source.LocalFile.Images[0].Wcs.PixelToWorld(x, y);
        return source.Bind(new CutoutSpec { Region = SkyRegion.Circle(ra, dec, radiusPixels * Scale) });
    }

    private (string Path, LocalCutPlan Plan) Cut(LocalCutoutSource source, CutoutSpec spec, string name = "cut.fits")
    {
        var plan = LocalCutPlan.For(source.LocalFile, spec);
        var path = Path.Combine(_dir, name);
        using var input = FitsContainer.OpenFits(source.LocalFile.Path);
        using (var output = File.Create(path)) FitsCutter.Write(input, plan, output);
        return (path, plan);
    }

    private static IReadOnlyList<FitsHduLayout> Layout(string path)
    {
        using var stream = File.OpenRead(path);
        return FitsLayout.Read(stream);
    }

    private static byte[] DataOf(string path, FitsHduLayout hdu)
    {
        using var stream = File.OpenRead(path);
        stream.Position = hdu.DataStart;
        var data = new byte[hdu.DataBytes];
        stream.ReadExactly(data);
        return data;
    }

    private string SingleImage(int bitpix, int width, int height, IEnumerable<string>? extra = null, Func<int, int, int, double>? value = null)
    {
        var cards = ImageCards(bitpix, primary: true, width, height);
        cards.AddRange(TanWcs(width / 2.0 + 0.5, height / 2.0 + 0.5, 150.0, 2.2, Scale));
        cards.AddRange(extra ?? []);
        return Write(_dir, $"img{bitpix}.fits", Hdu(cards, Pixels(bitpix, width, height, value ?? ((x, y, _) => x + 3 * y))));
    }

    // ── The pixels ───────────────────────────────────────────────────────────

    /// <summary>
    /// Every pixel of the box, byte for byte, at every BITPIX FITS has — copied, never converted, so
    /// BZERO and BSCALE still mean what they meant.
    /// </summary>
    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(-32)]
    [InlineData(-64)]
    public void ACut_KeepsEveryPixelAsItWas_AtEveryBitpix(int bitpix)
    {
        const int width = 60, height = 40;
        double Value(int x, int y, int _) => bitpix switch
        {
            8 => (x * 7 + y * 13) % 250,
            16 => x * 300 - y * 11,
            32 or 64 => x * 100_000 + y,
            _ => x + y / 64.0,
        };
        var path = SingleImage(bitpix, width, height, bitpix == 16 ? [Card("BZERO", 32768L), Card("BSCALE", 1L)] : null, Value);
        var source = Source(path);
        Assert.Null(source.Unavailable);

        var (cut, plan) = Cut(source, CircleAt(source, 20, 15, 5));

        var box = plan.Cuts.Single().Box;
        Assert.Equal(new PixelBox(15, 10, 25, 20), box);
        var original = DataOf(path, Layout(path)[0]);
        var written = DataOf(cut, Layout(cut)[0]);
        var size = Math.Abs(bitpix) / 8;
        Assert.Equal(box.Width * box.Height * size, written.Length);
        for (var row = 0; row < box.Height; row++)
        {
            var from = ((long)(box.Y0 - 1 + row) * width + box.X0 - 1) * size;
            Assert.Equal(original.AsSpan((int)from, box.Width * size).ToArray(), written.AsSpan(row * box.Width * size, box.Width * size).ToArray());
        }
    }

    /// <summary>Read back as the viewer reads it, a 16-bit image with BZERO has the physical values it had.</summary>
    [Fact]
    public void ACutOfAScaledImage_ReadsBackToTheSameValues()
    {
        var path = SingleImage(16, 60, 40, [Card("BZERO", 32768L), Card("BSCALE", 2L)], (x, y, _) => x * 300 - y * 11);
        var source = Source(path);
        var (cut, plan) = Cut(source, CircleAt(source, 30, 20, 8));

        var before = FitsParser.Parse(File.OpenRead(path), availableMemory: long.MaxValue)[0].ImageData!;
        var after = FitsParser.Parse(File.OpenRead(cut), availableMemory: long.MaxValue)[0].ImageData!;
        var box = plan.Cuts.Single().Box;
        for (var y = 0; y < box.Height; y++)
            for (var x = 0; x < box.Width; x++)
                Assert.Equal(before.Pixels[(box.Y0 - 1 + y) * 60 + box.X0 - 1 + x], after.Pixels[y * box.Width + x]);
    }

    // ── The sky ──────────────────────────────────────────────────────────────

    /// <summary>
    /// A star keeps its place on the sky: the cut's own header, read by the same WCS code, puts the
    /// star's new pixel at the star's old position — with a plain TAN, with SIP distortion (measured
    /// from the reference pixel, so still right once it moves), and with PC and CDELT.
    /// </summary>
    [Theory]
    [InlineData("tan")]
    [InlineData("sip")]
    [InlineData("pc")]
    public void AStar_IsAtTheSameSkyPosition_BeforeAndAfterTheCut(string wcsKind)
    {
        const int width = 80, height = 60;
        var cards = ImageCards(-32, primary: true, width, height);
        cards.AddRange(wcsKind switch
        {
            "sip" => TanWcs(40.5, 30.5, 210.8, -47.3, Scale, rotationDeg: 25, sip: true),
            "pc" =>
            [
                Text("CTYPE1", "RA---TAN"), Text("CTYPE2", "DEC--TAN"), Card("CRPIX1", 40.5), Card("CRPIX2", 30.5),
                Card("CRVAL1", 10.68), Card("CRVAL2", 41.27), Card("CDELT1", -Scale), Card("CDELT2", Scale),
                Card("PC1_1", 0.866), Card("PC1_2", -0.5), Card("PC2_1", 0.5), Card("PC2_2", 0.866),
            ],
            _ => TanWcs(40.5, 30.5, 150.0, 2.2, Scale, rotationDeg: 10),
        });
        var path = Write(_dir, "star.fits", Hdu(cards, Pixels(-32, width, height, (x, y, _) => x * y)));
        var source = Source(path);
        const double starX = 61.37, starY = 47.81; // far from the reference pixel, where distortion is largest

        var (cut, plan) = Cut(source, CircleAt(source, starX, starY, 6));

        var box = plan.Cuts.Single().Box;
        var before = source.LocalFile.Images[0].Wcs;
        var after = WcsInfo.FromHeader(Layout(cut)[0].Header);
        var (ra0, dec0) = before.PixelToWorld(starX, starY);
        var (ra1, dec1) = after.PixelToWorld(starX - (box.X0 - 1), starY - (box.Y0 - 1));
        Assert.Equal(ra0, ra1, 10);
        Assert.Equal(dec0, dec1, 10);

        var back = after.WorldToPixel(ra0, dec0)!.Value;
        Assert.Equal(starX - (box.X0 - 1), back.Px, 6);
        Assert.Equal(starY - (box.Y0 - 1), back.Py, 6);
    }

    /// <summary>
    /// A box on the sky across RA 0°: pixels a hair either side of 0° are neighbours on the image, not
    /// 360° apart.
    /// </summary>
    [Fact]
    public void ARegionAcrossRaZero_IsCutWhereItIs()
    {
        var cards = ImageCards(-32, primary: true, 60, 40);
        cards.AddRange(TanWcs(30.5, 20.5, 0.0, 10.0, Scale));
        var source = Source(Write(_dir, "zero.fits", Hdu(cards, Pixels(-32, 60, 40, (x, y, _) => x))));

        var spec = source.Bind(new CutoutSpec { Region = SkyRegion.Box(359.9995, 10.0, 8 * Scale, 8 * Scale) });
        var box = LocalCutPlan.For(source.LocalFile, spec).Cuts.Single().Box;

        Assert.InRange((box.X0 + box.X1) / 2.0, 34, 37);
        Assert.InRange(box.Width, 8, 11);
        Assert.InRange(box.Height, 8, 11);
    }

    [Fact]
    public void ARegionPartlyOffTheImage_IsCutToTheImage_AndSaysSo()
    {
        var source = Source(SingleImage(-32, 60, 40));
        var spec = CircleAt(source, 2, 3, 6);

        var check = source.Check(spec);
        var box = LocalCutPlan.For(source.LocalFile, spec).Cuts.Single().Box;

        Assert.True(check.IsValid);
        Assert.NotEmpty(check.Warnings);
        Assert.Equal(1, box.X0);
        Assert.Equal(1, box.Y0);
    }

    [Fact]
    public void ARegionOffTheImage_IsRefused()
    {
        var source = Source(SingleImage(-32, 60, 40));

        var check = source.Check(source.Bind(new CutoutSpec { Region = SkyRegion.Circle(151, 2.2, 0.001) }));

        Assert.Contains("outside", Assert.Single(check.Errors));
        Assert.Null(source.EstimateBytes(source.Bind(new CutoutSpec { Region = SkyRegion.Circle(151, 2.2, 0.001) })));
    }

    /// <summary>The estimate is not an estimate: it is the size of the file the cut writes.</summary>
    [Fact]
    public void TheSize_IsExactlyTheFileWritten()
    {
        var source = Source(SingleImage(-32, 60, 40));
        var spec = CircleAt(source, 30, 20, 7);

        var (cut, _) = Cut(source, spec);

        Assert.Equal(new FileInfo(cut).Length, source.EstimateBytes(spec));
    }

    // ── The header ───────────────────────────────────────────────────────────

    /// <summary>
    /// What the cut changes is changed — the size, every reference pixel, IRAF's offset, the sections,
    /// the checksums — and every other card is the file's own, byte for byte.
    /// </summary>
    [Fact]
    public void TheHeader_ChangesWhatTheCutChanges_AndNothingElse()
    {
        var kept = new[]
        {
            Text("OBJECT", "M31 field"),
            Free("HISTORY", "   an indented line a pipeline wrote"),
            Card("EXPTIME", "1200.50000", "seconds, as the pipeline wrote it"),
        };
        var path = SingleImage(-32, 60, 40,
        [
            .. kept,
            Card("CRPIX1A", 30.5), Card("CRPIX2A", 20.5), Card("LTV1", -32L), Card("LTV2", 0L),
            Text("DATASEC", "[11:50,1:40]"), Text("BIASSEC", "[1:10,1:40]"), Text("DETSEC", "[1:2048,1:4612]"),
            Text("CHECKSUM", "abcdefgh"), Text("DATASUM", "12345"),
        ]);
        var source = Source(path);

        var (cut, plan) = Cut(source, CircleAt(source, 20, 15, 5));

        var box = plan.Cuts.Single().Box;
        var (dx, dy) = (box.X0 - 1, box.Y0 - 1);
        var header = Layout(cut)[0].Header;
        var raw = Layout(cut)[0].RawCards;
        Assert.Equal(box.Width, header.NAxis1);
        Assert.Equal(box.Height, header.NAxis2);
        Assert.Equal(30.5 - dx, header.GetDouble("CRPIX1"));
        Assert.Equal(20.5 - dy, header.GetDouble("CRPIX2"));
        Assert.Equal(30.5 - dx, header.GetDouble("CRPIX1A"));
        Assert.Equal(-32 - dx, header.GetDouble("LTV1"));
        Assert.Equal(-dy, header.GetDouble("LTV2"));
        Assert.Equal($"[{15 - dx}:{box.X1 - dx},1:{box.Height}]", header.GetString("DATASEC"));
        Assert.False(header.Contains("BIASSEC")); // the cut misses the overscan
        Assert.False(header.Contains("DETSEC"));
        Assert.NotEqual("abcdefgh", header.GetString("CHECKSUM")); // the old data's sums, replaced by the new's
        Assert.NotEqual("12345", header.GetString("DATASUM"));
        foreach (var card in kept) Assert.Contains(card, raw);
        var history = string.Join(" ", raw.Where(c => c.StartsWith("HISTORY ")).Select(c => c[8..].Trim()));
        Assert.Contains("Cut out by Verbinal on ", history);
        Assert.Contains("from img-32.fits[0], pixels [15:25,10:20].", history);
        Assert.Contains("Region (ICRS, degrees): CIRCLE ", history);
        Assert.All(raw, c => Assert.Equal(80, c.Length));
    }

    /// <summary>
    /// A cut of a file that did not say where its pixels came from now does, as IRAF and DS9 read it:
    /// the cut's pixel (1, 1) is, "physically", the file's pixel at the box's corner.
    /// </summary>
    [Fact]
    public void TheCut_SaysWhereItSitsInTheFile_InThePhysicalPixelsDs9Shows()
    {
        var source = Source(SingleImage(-32, 60, 40));

        var (cut, plan) = Cut(source, CircleAt(source, 20, 15, 5));

        var box = plan.Cuts.Single().Box;
        var header = Layout(cut)[0].Header;
        double Physical(int axis, double image) => (image - header.GetDouble($"LTV{axis}")) / header.GetDouble($"LTM{axis}_{axis}");
        Assert.Equal(box.X0, Physical(1, 1));
        Assert.Equal(box.Y0, Physical(2, 1));
        Assert.Equal(box.X1, Physical(1, box.Width));
    }

    /// <summary>Every HDU of a cut — a multi-extension one's primary header too — checks out as fitsverify would check it.</summary>
    [Fact]
    public void EveryHduOfACut_HasTrueChecksums()
    {
        var source = Source(HstLike(gapPixels: 20));
        var (cut, _) = Cut(source, CircleAt(source, 25, 20, 5));

        var bytes = File.ReadAllBytes(cut);
        var hdus = Layout(cut);
        Assert.Equal(3, hdus.Count);
        long start = 0; // each HDU begins where the one before it ends
        for (var i = 0; i < hdus.Count; i++)
        {
            var end = hdus[i].DataStart + FitsParser.AlignToBlock(hdus[i].DataBytes);
            Assert.Equal(0xFFFFFFFFu, FitsChecksum.Sum(bytes.AsSpan((int)start, (int)(end - start))));
            start = end;
            var data = bytes.AsSpan((int)hdus[i].DataStart, (int)FitsParser.AlignToBlock(hdus[i].DataBytes));
            Assert.Equal(FitsChecksum.Sum(data).ToString(), hdus[i].Header.GetString("DATASUM"));
        }
    }

    // ── Multi-extension files ────────────────────────────────────────────────

    /// <summary>
    /// An HST-like frame: two chips, each a SCI and an ERR with its own sky coordinates, and a table. A
    /// region on one chip keeps that chip's SCI and ERR, cut by each one's own WCS, under the file's own
    /// primary header, which says what was left out.
    /// </summary>
    [Fact]
    public void AMultiExtensionFile_KeepsTheImagesTheRegionFallsOn()
    {
        var path = HstLike(gapPixels: 20);
        var source = Source(path);
        Assert.Equal(4, source.LocalFile.Images.Count);
        Assert.Equal(4, source.File.Parts.Count);

        var (cut, plan) = Cut(source, CircleAt(source, 25, 20, 5));

        var hdus = Layout(cut);
        Assert.Equal(3, hdus.Count);
        Assert.Equal(0, hdus[0].Header.NAxis);
        Assert.Equal(2, hdus[0].Header.GetInt("NEXTEND"));
        Assert.Equal("HST", hdus[0].Header.GetString("TELESCOP"));
        var history = string.Concat(hdus[0].RawCards);
        Assert.Contains("[SCI,2]", history);
        Assert.Contains("[ERR,2]", history);
        Assert.Contains("[HDRLET]", history);
        Assert.Equal(("SCI", 1), (hdus[1].Header.GetString("EXTNAME"), hdus[1].Header.GetInt("EXTVER")));
        Assert.Equal(("ERR", 1), (hdus[2].Header.GetString("EXTNAME"), hdus[2].Header.GetInt("EXTVER")));
        Assert.Equal("IMAGE", hdus[1].Header.GetString("XTENSION"));
        Assert.Equal(plan.Cuts.First().Box.Width, hdus[1].Header.NAxis1);

        // And it opens in the viewer.
        var parsed = FitsParser.Parse(File.OpenRead(cut), availableMemory: long.MaxValue);
        Assert.Equal(3, parsed.Count);
        Assert.NotNull(parsed[1].ImageData);
    }

    /// <summary>The images to keep, chosen: only those are cut; the rest are named as left out.</summary>
    [Fact]
    public void ChosenImages_AreTheOnlyOnesCut()
    {
        var source = Source(HstLike(gapPixels: 20));
        Assert.Equal(new[] { "SCI,1", "ERR,1", "SCI,2", "ERR,2" }, source.File.Extensions);

        var spec = CircleAt(source, 25, 20, 5) with { Extensions = ["SCI,1"] };
        Assert.True(source.Check(spec).IsValid);
        var (cut, _) = Cut(source, spec);

        var hdus = Layout(cut);
        Assert.Equal(2, hdus.Count);
        Assert.Equal("SCI,1", hdus[1].Id);
        Assert.Equal(1, hdus[0].Header.GetInt("NEXTEND"));
        Assert.Contains("[ERR,1]", string.Concat(hdus[0].RawCards));
    }

    [Fact]
    public void AnImageTheFileDoesNotHave_IsRefusedWithTheOnesItHas()
    {
        var source = Source(HstLike(gapPixels: 20));
        var error = Assert.Single(source.Check(CircleAt(source, 25, 20, 5) with { Extensions = ["DQ,1"] }).Errors);
        Assert.Contains("no image DQ,1", error);
        Assert.Contains("SCI,1, ERR,1, SCI,2, ERR,2", error);
    }

    /// <summary>A region on chip 1 with only chip 2 chosen is on none of the chosen — said so, not "on none of the file's".</summary>
    [Fact]
    public void ARegionOnNoneOfTheChosenImages_SaysSo()
    {
        var source = Source(HstLike(gapPixels: 20));
        Assert.Contains("none of the images chosen",
            Assert.Single(source.Check(CircleAt(source, 25, 20, 5) with { Extensions = ["SCI,2", "ERR,2"] }).Errors));
    }

    /// <summary>A single image offers no choice; CADC's cut offers none either — asking for one is refused, not ignored.</summary>
    [Fact]
    public void AChoiceWhereThereIsNone_IsRefused()
    {
        var single = Source(SingleImage(-32, 60, 40));
        Assert.Empty(single.File.Extensions);
        Assert.Contains("cannot choose", Assert.Single(single.Check(CircleAt(single, 30, 20, 4) with { Extensions = ["0"] }).Errors));
    }

    /// <summary>A primary image with a table after it: the image is cut, and its header no longer counts the table.</summary>
    [Fact]
    public void APrimaryImageCut_CountsOnlyTheExtensionsCutWithIt()
    {
        var cards = ImageCards(-32, primary: true, 60, 40);
        cards.AddRange(TanWcs(30.5, 20.5, 150.0, 2.2, Scale));
        cards.AddRange([Card("EXTEND", "T"), Card("NEXTEND", 1L)]);
        var table = Hdu([Text("XTENSION", "BINTABLE"), Card("BITPIX", 8L), Card("NAXIS", 2L), Card("NAXIS1", 4L),
                         Card("NAXIS2", 1L), Card("PCOUNT", 0L), Card("GCOUNT", 1L), Card("TFIELDS", 1L), Text("TFORM1", "1J")], new byte[4]);
        var source = Source(Write(_dir, "with-table.fits", Hdu(cards, Pixels(-32, 60, 40, (x, y, _) => x)), table));

        var (cut, _) = Cut(source, CircleAt(source, 30, 20, 4));

        var hdus = Layout(cut);
        Assert.Single(hdus);
        Assert.Equal(0, hdus[0].Header.GetInt("NEXTEND"));
    }

    /// <summary>The outline of two chips holds the gap between them; a region in the gap is on neither, and is refused.</summary>
    [Fact]
    public void ARegionInTheGapBetweenChips_IsRefused()
    {
        var source = Source(HstLike(gapPixels: 30));
        // Chip 1's rows run 1–40; chip 2 starts 30 pixels above its top.
        var spec = CircleAt(source, 25, 55, 4);

        var check = source.Check(spec);

        Assert.Contains("none of this file's images", Assert.Single(check.Errors));
    }

    /// <summary>Two 50 × 40 chips, one above the other with a gap, each with a SCI and an ERR, and a table.</summary>
    private string HstLike(int gapPixels)
    {
        var primary = Hdu([Card("SIMPLE", "T"), Card("BITPIX", 8L), Card("NAXIS", 0L), Card("EXTEND", "T"),
                           Card("NEXTEND", 5L), Text("TELESCOP", "HST")]);
        byte[] Chip(string name, int ver)
        {
            var cards = ImageCards(-32, primary: false, 50, 40);
            cards.AddRange([Text("EXTNAME", name), Card("EXTVER", (long)ver)]);
            // Chip 2 lies (40 + gap) pixels north of chip 1.
            cards.AddRange(TanWcs(25.5, 20.5 - (ver - 1) * (40 + gapPixels), 150.0, 2.2, Scale));
            return Hdu(cards, Pixels(-32, 50, 40, (x, y, _) => ver * 1000 + x + y));
        }
        var table = Hdu([Text("XTENSION", "BINTABLE"), Card("BITPIX", 8L), Card("NAXIS", 2L), Card("NAXIS1", 4L),
                         Card("NAXIS2", 1L), Card("PCOUNT", 0L), Card("GCOUNT", 1L), Card("TFIELDS", 1L),
                         Text("TFORM1", "1J"), Text("EXTNAME", "HDRLET")], new byte[4]);
        return Write(_dir, "hst_flt.fits", primary, Chip("SCI", 1), Chip("ERR", 1), Chip("SCI", 2), Chip("ERR", 2), table);
    }

    // ── Cubes ────────────────────────────────────────────────────────────────

    private const double C = 299_792_458.0;

    /// <summary>A 30 × 20 × 60 cube on a frequency axis, 345.0 GHz up in 0.1 GHz channels; each voxel says where it is.</summary>
    private LocalCutoutSource Cube(params string[] extra)
    {
        var cards = ImageCards(-32, primary: true, 30, 20, 60);
        cards.AddRange(TanWcs(15.5, 10.5, 83.8, -5.4, Scale));
        cards.AddRange([Text("CTYPE3", "FREQ"), Text("CUNIT3", "Hz"), Card("CRPIX3", 1.0), Card("CRVAL3", 345.0e9), Card("CDELT3", 1.0e8), .. extra]);
        return Source(Write(_dir, "cube.fits", Hdu(cards, Pixels(-32, 30, 20, (x, y, z) => x + 100 * y + 10_000 * z, planes: 60))));
    }

    [Fact]
    public void ACube_CanBeCutByWavelength_OverTheRangeItCovers()
    {
        var file = Cube().File;

        Assert.True(file.Supports("BAND"));
        Assert.Equal(C / (345.0e9 + 59.5e8), file.BandMin!.Value, 12);
        Assert.Equal(C / (345.0e9 - 0.5e8), file.BandMax!.Value, 12);
    }

    /// <summary>
    /// A box on the sky and a band: the planes the band reaches, the box on each — every voxel where it
    /// was — and each plane still at its own frequency.
    /// </summary>
    [Fact]
    public void ACubeCut_ByBoxAndBand_KeepsThosePlanes_AtTheirOwnFrequencies()
    {
        var source = Cube();
        var spec = CircleAt(source, 15, 10, 4) with { BandMin = C / 348.95e9, BandMax = C / 347.95e9 }; // channels 31–40: 348.0–348.9 GHz

        var (cut, plan) = Cut(source, spec);

        var box = plan.Cuts.Single().Box;
        var hdu = Layout(cut)[0];
        Assert.Equal(10, hdu.Header.GetInt("NAXIS3"));
        Assert.Equal(1.0 - 30, hdu.Header.GetDouble("CRPIX3"));
        var after = CanfarDesktop.Models.Fits.SpectralAxis.Find(hdu.Header)!;
        Assert.Equal(C / 348.0e9, after.WavelengthAt(1)!.Value, 15); // plane 1 of the cut is plane 31 of the cube
        Assert.Contains("planes 31-40 of 60 (axis 3)", string.Join(" ", hdu.RawCards.Where(c => c.StartsWith("HISTORY")).Select(c => c[8..].Trim())));

        var data = DataOf(cut, hdu);
        float Voxel(int x, int y, int z) => System.Buffers.Binary.BinaryPrimitives.ReadSingleBigEndian(
            data.AsSpan(((z * box.Height + y) * box.Width + x) * 4));
        Assert.Equal(box.X0 - 1 + 100 * (box.Y0 - 1) + 10_000 * 30, Voxel(0, 0, 0));
        Assert.Equal(box.X1 - 1 + 100 * (box.Y1 - 1) + 10_000 * 39, Voxel(box.Width - 1, box.Height - 1, 9));
    }

    [Fact]
    public void ACubeCut_OnTheSkyAlone_KeepsEveryPlane()
    {
        var source = Cube();

        var (cut, _) = Cut(source, CircleAt(source, 15, 10, 4));

        Assert.Equal(60, Layout(cut)[0].Header.GetInt("NAXIS3"));
        Assert.Equal(1.0, Layout(cut)[0].Header.GetDouble("CRPIX3"));
    }

    /// <summary>A band alone — no region — is the whole of each plane it reaches.</summary>
    [Fact]
    public void ABandAlone_CutsWholePlanes()
    {
        var source = Cube();
        var spec = source.Bind(new CutoutSpec { BandMin = C / 345.25e9, BandMax = C / 344.95e9 }); // channels 1–3

        Assert.True(source.Check(spec).IsValid);
        var (cut, _) = Cut(source, spec);

        var header = Layout(cut)[0].Header;
        Assert.Equal((30, 20, 3), (header.NAxis1, header.NAxis2, header.GetInt("NAXIS3")));
    }

    [Fact]
    public void ABandTheCubeDoesNotCover_IsRefused()
    {
        var source = Cube();
        Assert.Contains("wavelength range is outside",
            Assert.Single(source.Check(source.Bind(new CutoutSpec { BandMin = 1e-6, BandMax = 2e-6 })).Errors));
    }

    // ── Tile-compressed files ────────────────────────────────────────────────

    /// <summary>
    /// A MegaPrime-like .fz: a dataless primary, and a 16-bit CCD compressed in RICE_1 tiles that do not
    /// divide the image evenly. Cut, it is a plain image whose every pixel is the one the viewer
    /// decompresses — and whose header is the image's, not the table's.
    /// </summary>
    [Fact]
    public void ARiceCompressedFile_IsCutToAPlainImage_OfTheSamePixels()
    {
        const int width = 40, height = 30;
        var pixels = new short[width * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++) pixels[y * width + x] = (short)(x * 17 + y * 5 - 300 + (x * y % 7));
        var image = TanWcs(20.5, 15.5, 150.0, 2.2, Scale);
        image.AddRange([Card("BZERO", 32768L), Card("BSCALE", 1L), Text("FILTER", "r.MP9602")]);
        var path = Write(_dir, "1234567o.fits.fz",
            Hdu([Card("SIMPLE", "T"), Card("BITPIX", 16L), Card("NAXIS", 0L), Card("EXTEND", "T"), Card("NEXTEND", 1L)]),
            RiceCompressed(image, pixels, width, height, tileWidth: 16, tileHeight: 8));
        var source = Source(path);
        Assert.Null(source.Unavailable);

        var (cut, plan) = Cut(source, CircleAt(source, 22, 14, 6));

        var box = plan.Cuts.Single().Box;
        var hdus = Layout(cut);
        Assert.Equal(2, hdus.Count);
        var header = hdus[1].Header;
        Assert.Equal(("IMAGE", 16, 2, box.Width, box.Height), (header.GetString("XTENSION"), header.BitPix, header.NAxis, header.NAxis1, header.NAxis2));
        Assert.Equal(32768, header.BZero);
        Assert.Equal("r.MP9602", header.GetString("FILTER"));
        Assert.Equal("ccd00", header.GetString("EXTNAME"));
        Assert.DoesNotContain(hdus[1].RawCards, c => c.StartsWith("Z") || c.StartsWith("TFORM") || c.StartsWith("TTYPE") || c.StartsWith("TFIELDS"));
        Assert.Equal(20.5 - (box.X0 - 1), header.GetDouble("CRPIX1"));

        var data = DataOf(cut, hdus[1]);
        for (var y = 0; y < box.Height; y++)
            for (var x = 0; x < box.Width; x++)
                Assert.Equal(pixels[(box.Y0 - 1 + y) * width + box.X0 - 1 + x],
                    System.Buffers.Binary.BinaryPrimitives.ReadInt16BigEndian(data.AsSpan((y * box.Width + x) * 2)));

        // And the viewer, decompressing the whole file its own way, sees the same pixels at the same place.
        var viewed = FitsParser.Parse(File.OpenRead(path), availableMemory: long.MaxValue)[1].ImageData!;
        var readBack = FitsParser.Parse(File.OpenRead(cut), availableMemory: long.MaxValue)[1].ImageData!;
        Assert.Equal(viewed.Pixels[(box.Y0 - 1) * width + box.X0 - 1], readBack.Pixels[0]);
    }

    // ── What cannot be cut, and why ──────────────────────────────────────────

    [Fact]
    public void AnImageWithoutSkyCoordinates_SaysSo()
        => Assert.Contains("no sky coordinates",
            Source(Write(_dir, "bare.fits", Hdu(ImageCards(16, true, 10, 10), new byte[200]))).Unavailable);

    [Fact]
    public void CoordinatesRebuiltFromRaAndDec_AreTooRoughToCutBy()
    {
        var cards = ImageCards(16, true, 10, 10);
        cards.AddRange([Text("RA", "10:00:00"), Text("DEC", "+02:00:00")]);
        Assert.Contains("approximate", Source(Write(_dir, "legacy.fits", Hdu(cards, new byte[200]))).Unavailable);
    }

    [Fact]
    public void AGalacticImage_SaysItIsNotInRaAndDec()
    {
        var cards = ImageCards(16, true, 10, 10);
        cards.AddRange(TanWcs(5, 5, 120, 1, Scale).Select(c => c.Replace("RA---TAN", "GLON-TAN").Replace("DEC--TAN", "GLAT-TAN")));
        Assert.Contains("GLON and GLAT", Source(Write(_dir, "gal.fits", Hdu(cards, new byte[200]))).Unavailable);
    }

    [Fact]
    public void B1950Coordinates_AreNotTakenForIcrs()
    {
        var cards = ImageCards(16, true, 10, 10);
        cards.AddRange(TanWcs(5, 5, 120, 1, Scale).Select(c => c.Replace("ICRS", "FK4")));
        Assert.Contains("B1950", Source(Write(_dir, "fk4.fits", Hdu(cards, new byte[200]))).Unavailable);
    }

    [Fact]
    public void ATileCompressedImage_SaysSo_WithWhatToDo()
    {
        var header = Hdu([Text("XTENSION", "BINTABLE"), Card("BITPIX", 8L), Card("NAXIS", 2L), Card("NAXIS1", 8L),
                          Card("NAXIS2", 1L), Card("PCOUNT", 0L), Card("GCOUNT", 1L), Card("TFIELDS", 1L),
                          Card("ZIMAGE", "T"), Text("ZCMPTYPE", "GZIP_1"), Card("ZBITPIX", 16L), Card("ZNAXIS", 2L),
                          Card("ZNAXIS1", 10L), Card("ZNAXIS2", 10L)], new byte[8]);
        var path = Write(_dir, "packed.fits.fz", Hdu([Card("SIMPLE", "T"), Card("BITPIX", 8L), Card("NAXIS", 0L)]), header);

        var why = Source(path).Unavailable;

        Assert.Contains("GZIP_1", why);
        Assert.Contains("funpack", why);
    }

    [Fact]
    public void AFileThatIsNotFits_SaysSo()
    {
        var path = Path.Combine(_dir, "page.fits");
        File.WriteAllText(path, "<html>not found</html>");
        Assert.Contains("cannot be read as FITS", Source(path).Unavailable);
    }

    [Fact]
    public void AFileCutShort_SaysItMayNotHaveFinishedDownloading()
    {
        var full = File.ReadAllBytes(SingleImage(-32, 60, 40));
        var path = Path.Combine(_dir, "short.fits");
        File.WriteAllBytes(path, full[..(2880 * 2)]);
        Assert.Contains("finished downloading", Source(path).Unavailable);
    }

    // ── Making it, as the app does ───────────────────────────────────────────

    [Fact]
    public async Task TheMaker_CutsTheDownloadedFile_IntoItsTarget()
    {
        var path = SingleImage(-32, 60, 40);
        var source = Source(path);
        var spec = CircleAt(source, 30, 20, 5);
        var target = Path.Combine(_dir, "made.fits");
        var maker = new LocalCutoutMaker((_, _) => path);
        var stages = new List<string>();

        await maker.MakeAsync(new CutoutJob("ivo://cadc/T", spec, target), new Collect(stages), new Progress<(long, long?)>());

        Assert.Equal(source.EstimateBytes(spec), new FileInfo(target).Length);
        Assert.Equal(new[] { "reading the file", "cutting" }, stages);
        Assert.False(File.Exists(target + ".tmp"));
    }

    [Fact]
    public async Task TheMaker_SaysSo_WhenTheFileIsGone()
    {
        var spec = new CutoutSpec { ArtifactId = "cadc:TEST/x.fits", Region = SkyRegion.Circle(1, 2, 0.01), CutBy = CutoutMethod.Local };
        var maker = new LocalCutoutMaker((_, _) => null);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            maker.MakeAsync(new CutoutJob("ivo://cadc/T", spec, Path.Combine(_dir, "x.fits")), new Collect([]), new Progress<(long, long?)>()));

        Assert.Contains("not on this computer", error.Message);
    }

    [Fact]
    public async Task TheMaker_NeverWritesOverTheFileItCuts()
    {
        var path = SingleImage(-32, 60, 40);
        var before = File.ReadAllBytes(path);
        var spec = CircleAt(Source(path), 30, 20, 5);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new LocalCutoutMaker((_, _) => path).MakeAsync(new CutoutJob("p", spec, path), new Collect([]), new Progress<(long, long?)>()));

        Assert.Contains("over the file", error.Message);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    private sealed class Collect(List<string> into) : IProgress<string>
    {
        public void Report(string value) => into.Add(value);
    }

    // ── Which downloaded file is which archive file ──────────────────────────

    [Fact]
    public void TheDownloadedFile_IsKnownByWhatItWasDownloadedAs_OrElseByItsName()
    {
        var path = SingleImage(-32, 10, 10);
        var named = new DownloadedObservation { PublisherID = "p", LocalPath = path, ArtifactId = "cadc:HST/j_flt.fits" };
        var byName = new DownloadedObservation { PublisherID = "q", LocalPath = path };
        var cutout = new DownloadedObservation { PublisherID = "r", LocalPath = path, Cutout = new CutoutSpec { ArtifactId = "cadc:X/img-32.fits" } };
        var gone = new DownloadedObservation { PublisherID = "s", LocalPath = Path.Combine(_dir, "gone.fits") };
        DownloadedObservation[] records = [named, byName, cutout, gone];

        Assert.Same(named, LocalCopies.Find(records, "p", "cadc:HST/j_flt.fits"));
        Assert.Null(LocalCopies.Find(records, "p", "cadc:HST/j_flc.fits"));          // the same name is not enough when it says which
        Assert.Same(byName, LocalCopies.Find(records, "q", "cadc:CFHT/img-32.fits"));  // a record from before: by its name
        Assert.Null(LocalCopies.Find(records, "r", "cadc:X/img-32.fits"));            // a cutout is not the whole file
        Assert.Null(LocalCopies.Find(records, "s", null));                              // not on this computer
        Assert.Same(byName, LocalCopies.Find(records, "q", null));                      // whichever the observation's file is
        Assert.Equal("cadc:CFHT/img-32.fits", LocalCopies.ArtifactOf(byName, ["cadc:CFHT/other.fits", "cadc:CFHT/img-32.fits"]));
        Assert.Equal("", LocalCopies.ArtifactOf(byName, ["cadc:CFHT/other.fits"]));
    }

    // ── A downloaded copy nobody named ──────────────────────────────────────

    private static CanfarDesktop.Models.Caom2.CAOM2Observation Observation(params (string Uri, long Bytes)[] files) => new()
    {
        Collection = "CFHTSG",
        ObservationID = "t",
        Planes = [new CanfarDesktop.Models.Caom2.Caom2Plane { Artifacts = files.Select(f => new CanfarDesktop.Models.Caom2.Caom2Artifact { Uri = f.Uri, ContentLength = f.Bytes }).ToList() }],
    };

    /// <summary>
    /// A copy downloaded under another name, with no record of which file it is, is shown with the file
    /// it is exactly the size of — and only when no other file of the observation's is that size too.
    /// It stays "the observation's downloaded file", which is how the cut finds it again.
    /// </summary>
    [Fact]
    public void AnUnnamedCopy_IsShownWithTheFileItIsExactlyTheSizeOf()
    {
        var source = new LocalCutoutSource(LocalFitsFile.Inspect(SingleImage(-32, 20, 10), ""));
        var bytes = source.LocalFile.FileBytes;

        Assert.Same(source, Assert.Single(CutoutSources.For([source], "cadc:CFHTSG/t.fits", Observation(("cadc:CFHTSG/t.fits", bytes), ("cadc:CFHTSG/t.weight.fits", bytes + 1)))));
        Assert.Empty(CutoutSources.For([source], "cadc:CFHTSG/t.weight.fits", Observation(("cadc:CFHTSG/t.fits", bytes), ("cadc:CFHTSG/t.weight.fits", bytes + 1))));
        Assert.Empty(CutoutSources.For([source], "cadc:CFHTSG/t.fits", Observation(("cadc:CFHTSG/t.fits", bytes), ("cadc:CFHTSG/u.fits", bytes)))); // two that size: not a guess
        Assert.Empty(CutoutSources.For([source], "cadc:CFHTSG/t.fits"));                                                                   // no sizes to go by
        Assert.Equal("", source.Bind(new CutoutSpec()).ArtifactId);
    }

    /// <summary>The file on this computer is read once while it stays as it is, and again when it changes or goes.</summary>
    [Fact]
    public void TheLocalCopy_IsReadAgainOnlyWhenItChanges()
    {
        var path = SingleImage(-32, 20, 10);
        var record = new DownloadedObservation { PublisherID = "p", LocalPath = path, ArtifactId = "cadc:X/img-32.fits" };
        var cache = new LocalSourceCache();

        var first = cache.Get([record], "p", ["cadc:X/img-32.fits"]);
        Assert.NotNull(first);
        Assert.Same(first, cache.Get([record], "p", ["cadc:X/img-32.fits"]));

        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(1)); // replaced
        var second = cache.Get([record], "p", ["cadc:X/img-32.fits"]);
        Assert.NotSame(first, second);

        File.Delete(path);
        Assert.Null(cache.Get([record], "p", ["cadc:X/img-32.fits"]));
    }
}
