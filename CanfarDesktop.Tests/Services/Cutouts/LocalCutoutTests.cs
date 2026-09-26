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
        Assert.False(header.Contains("CHECKSUM"));
        Assert.False(header.Contains("DATASUM"));
        foreach (var card in kept) Assert.Contains(card, raw);
        var history = string.Join(" ", raw.Where(c => c.StartsWith("HISTORY ")).Select(c => c[8..].Trim()));
        Assert.Contains("Cut out by Verbinal on ", history);
        Assert.Contains("from img-32.fits[0], pixels [15:25,10:20].", history);
        Assert.Contains("Region (ICRS, degrees): CIRCLE ", history);
        Assert.All(raw, c => Assert.Equal(80, c.Length));
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
}
