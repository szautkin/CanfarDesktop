using System.Buffers.Binary;
using System.Text;
using Xunit;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Models.Cutouts;
using CanfarDesktop.Models.Fits;
using CanfarDesktop.Services;
using CanfarDesktop.Services.Cutouts;
using CanfarDesktop.Services.Cutouts.Local;
using CanfarDesktop.Services.Fits;
using static CanfarDesktop.Tests.Helpers.SyntheticFits;

namespace CanfarDesktop.Tests.Services.Cutouts;

/// <summary>
/// A weight map cut with its image: found beside it, proved to lie on the same pixels, and cut with the
/// very same box — so pixel (i, j) of the one cutout is the weight of pixel (i, j) of the other. Every
/// file is synthetic, so the test knows every pixel's value and where it is on the sky.
/// </summary>
public class LocalCompanionTests : IDisposable
{
    private const double Scale = 1e-4; // degrees per pixel
    private const int Width = 60, Height = 40;
    private const string ScienceId = "cadc:CFHTSG/tile.R.fits";
    private const string WeightId = "cadc:CFHTSG/tile.R.weight.fits";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "verbinal-companion-" + Guid.NewGuid().ToString("N")[..8]);

    public LocalCompanionTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    // ── Files ────────────────────────────────────────────────────────────────

    /// <summary>A 16-bit tile, its sky coordinates as a CD matrix; each pixel's value says where it is.</summary>
    private string Science(string name = "tile.R.fits")
    {
        var cards = ImageCards(16, primary: true, Width, Height);
        cards.AddRange(TanWcs(30.5, 20.5, 150.0, 2.2, Scale));
        return Write(_dir, name, Hdu(cards, Pixels(16, Width, Height, (x, y, _) => x + 100 * y)));
    }

    /// <summary>
    /// Its weight map, 32-bit float, with the same sky coordinates written the other way FITS allows —
    /// PC and CDELT — and its reference pixel moved by <paramref name="shift"/> pixels.
    /// </summary>
    private string Weight(string name = "tile.R.weight.fits", double shift = 0, int width = Width)
    {
        var cards = ImageCards(-32, primary: true, width, Height);
        cards.AddRange([
            Text("CTYPE1", "RA---TAN"), Text("CTYPE2", "DEC--TAN"),
            Card("CRPIX1", 30.5 + shift), Card("CRPIX2", 20.5), Card("CRVAL1", 150.0), Card("CRVAL2", 2.2),
            Card("CDELT1", -Scale), Card("CDELT2", Scale), Card("PC1_1", 1.0), Card("PC2_2", 1.0), Text("RADESYS", "ICRS"),
        ]);
        return Write(_dir, name, Hdu(cards, Pixels(-32, width, Height, WeightAt)));
    }

    private static double WeightAt(int x, int y, int plane) => 0.5 + x / 64.0 + y; // exact in a float

    private static LocalCutoutSource Source(string path, params string[] others)
        => new(LocalFitsFile.Inspect(path, ScienceId, [ScienceId, .. others]));

    private static CutoutSpec CircleAt(LocalCutoutSource source, double x, double y, double radiusPixels, params string[] companions)
    {
        var (ra, dec) = source.LocalFile.Images[0].Wcs.PixelToWorld(x, y);
        return source.Bind(new CutoutSpec { Region = SkyRegion.Circle(ra, dec, radiusPixels * Scale), Companions = companions });
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

    private static Task Make(LocalCutoutSource source, CutoutSpec spec, string target)
        => new LocalCutoutMaker((_, _) => source.LocalFile.Path)
            .MakeAsync(new CutoutJob("ivo://cadc/CFHTSG", spec, target), new Progress<string>(), new Progress<(long, long?)>());

    // ── Which files are companions ───────────────────────────────────────────

    /// <summary>The same grid is the same sky at every pixel — however the header says it.</summary>
    [Fact]
    public void AWeightMapOnTheSamePixels_IsOffered_ThoughItsHeaderSaysSoAnotherWay()
    {
        var science = Science();
        Weight();

        var companion = Assert.Single(Source(science, WeightId).File.Companions);

        Assert.Equal(new CutoutCompanion(WeightId, "tile.R.weight.fits"), companion);
    }

    /// <summary>
    /// A hundredth of a pixel apart is the same grid; a twentieth is not — and is not cut with a box
    /// that would put the weights a twentieth of a pixel off their pixels.
    /// </summary>
    [Theory]
    [InlineData(0.005, true)]
    [InlineData(0.05, false)]
    [InlineData(1.0, false)]
    public void TheSameGrid_IsTheSameToAHundredthOfAPixel(double shift, bool same)
    {
        var science = Science();
        Weight(shift: shift);

        var companion = Assert.Single(Source(science, WeightId).File.Companions);

        if (same) Assert.Null(companion.Unavailable);
        else Assert.Contains("not on the same pixels", companion.Unavailable);
    }

    [Fact]
    public void AnImageOfAnotherSize_IsNotOnTheSamePixels()
    {
        var science = Science();
        Weight(width: Width - 1);

        Assert.Contains("not on the same pixels", Assert.Single(Source(science, WeightId).File.Companions).Unavailable);
    }

    /// <summary>
    /// Only the observation's own FITS files, and only those beside the file: not a catalogue or a
    /// preview beside it, not a file of the observation that is not here, not the file itself.
    /// </summary>
    [Fact]
    public void OnlyTheObservationsOtherFitsFiles_BesideIt_AreOffered()
    {
        var science = Science();
        Weight();
        File.WriteAllText(Path.Combine(_dir, "tile.R.cat"), "# a catalogue");
        File.WriteAllBytes(Path.Combine(_dir, "tile.R.preview.png"), [0x89, 0x50]);
        Weight("unrelated.weight.fits"); // beside it, but not the observation's

        var source = Source(science, WeightId, "cadc:CFHTSG/tile.R.cat", "cadc:CFHTSG/tile.R.preview.png", "cadc:CFHTSG/tile.G.fits");

        Assert.Equal(new[] { WeightId }, source.File.Companions.Select(c => c.ArtifactId));
    }

    /// <summary>What was not asked for is not looked for: without the observation's files, no companions.</summary>
    [Fact]
    public void AFileReadWithoutItsObservationsFiles_HasNoCompanions()
    {
        var science = Science();
        Weight();

        Assert.Empty(LocalFitsFile.Inspect(science, ScienceId).Companions);
    }

    /// <summary>A weight map fpack stored as quantized floats is one the app cannot read — greyed, with why.</summary>
    [Fact]
    public void AWeightMapTheAppCannotDecompress_IsGreyed_WithWhy()
    {
        var science = Science();
        var image = new List<string>(TanWcs(30.5, 20.5, 150.0, 2.2, Scale));
        var compressed = RiceCompressed(image, new short[Width * Height], Width, Height, Width, 1);
        Replace(compressed, Card("ZBITPIX", 16L), Card("ZBITPIX", -32L));
        Write(_dir, "tile.R.weight.fits.fz", Hdu([Card("SIMPLE", "T"), Card("BITPIX", 8L), Card("NAXIS", 0L), Card("EXTEND", "T")]),
              compressed);

        var companion = Assert.Single(Source(science, "cadc:CFHTSG/tile.R.weight.fits.fz").File.Companions);

        Assert.Contains("tile-compressed", companion.Unavailable);
        Assert.Contains("-32", companion.Unavailable);
    }

    private static void Replace(byte[] bytes, string card, string with)
    {
        var at = Encoding.ASCII.GetString(bytes).IndexOf(card, StringComparison.Ordinal);
        Assert.True(at >= 0);
        Encoding.ASCII.GetBytes(with).CopyTo(bytes, at);
    }

    // ── The same box ─────────────────────────────────────────────────────────

    /// <summary>
    /// The weight map's cutout is the science cutout's box of the weight map: the same pixels, the same
    /// size, every weight the one at that pixel, and the same sky at the same pixel of both — and it
    /// says whose cutout it is the twin of.
    /// </summary>
    [Fact]
    public async Task TheWeightMap_IsCutWithTheIdenticalBox()
    {
        var science = Science();
        Weight();
        var source = Source(science, WeightId);
        var spec = CircleAt(source, 20, 15, 6, WeightId);
        var plan = LocalCutPlan.For(source.LocalFile, spec);
        var box = Assert.Single(plan.Cuts).Box;

        var target = Path.Combine(_dir, "cut.fits");
        await Make(source, spec, target);

        var weightCut = spec.CompanionPath(target, WeightId);
        var (cut, weight) = (Assert.Single(Layout(target)), Assert.Single(Layout(weightCut)));
        Assert.Equal((box.Width, box.Height), (weight.Header.NAxis1, weight.Header.NAxis2));
        Assert.Equal((cut.Header.NAxis1, cut.Header.NAxis2), (weight.Header.NAxis1, weight.Header.NAxis2));

        var data = DataOf(weightCut, weight);
        for (var j = 0; j < box.Height; j++)
            for (var i = 0; i < box.Width; i++)
            {
                var stored = BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(4 * (j * box.Width + i)));
                Assert.Equal(WeightAt(box.X0 - 1 + i, box.Y0 - 1 + j, 0), stored);
            }

        var (a, b) = (WcsInfo.FromHeader(cut.Header), WcsInfo.FromHeader(weight.Header));
        foreach (var (x, y) in new[] { (1.0, 1.0), (box.Width, box.Height), (3.5, 7.25) })
        {
            var (ra1, dec1) = a.PixelToWorld(x, y);
            var (ra2, dec2) = b.PixelToWorld(x, y);
            Assert.True(SkyGeometry.Distance(new SkyPoint(ra1, dec1), new SkyPoint(ra2, dec2)) < 1e-6 * Scale);
        }

        Assert.Contains("The same pixels as the cutout of tile.R.fits", HistoryOf(weight));
        Assert.Contains($"from tile.R.weight.fits[0], pixels {box}", HistoryOf(weight));
    }

    private static string HistoryOf(FitsHduLayout hdu)
        => string.Join(" ", hdu.RawCards.Where(c => c.StartsWith("HISTORY", StringComparison.Ordinal)).Select(c => c[8..].Trim())); // lines wrap at words

    /// <summary>
    /// A mosaic's weight map is matched CCD by CCD, by name — not by where each lies in the file — and
    /// each CCD's weights are cut with that CCD's box.
    /// </summary>
    [Fact]
    public void AMosaicsWeightMap_IsMatchedByName_CcdByCcd()
    {
        var science = Mosaic("mosaic.fits", -32, ["ccd00", "ccd01"], (ccd, x, y) => 1000 * ccd + x + y);
        Mosaic("mosaic.weight.fits", -32, ["ccd01", "ccd00"], (ccd, x, y) => -(1000 * ccd + x + y)); // the other order
        var source = new LocalCutoutSource(LocalFitsFile.Inspect(science, "cadc:CFHT/mosaic.fits", ["cadc:CFHT/mosaic.weight.fits"]));
        var ccd01 = source.LocalFile.Images[1];
        var (ra, dec) = ccd01.Wcs.PixelToWorld(25, 20);
        var spec = source.Bind(new CutoutSpec { Region = SkyRegion.Circle(ra, dec, 5 * Scale), Companions = ["cadc:CFHT/mosaic.weight.fits"] });

        var plan = LocalCutPlan.For(source.LocalFile, spec);
        var (_, weightPlan) = Assert.Single(plan.CompanionsOf(source.LocalFile, spec));

        var scienceCut = Assert.Single(plan.Cuts);
        var weightCut = Assert.Single(weightPlan.Cuts);
        Assert.Equal("ccd01", scienceCut.Image.Hdu.Id);
        Assert.Equal("ccd01", weightCut.Image.Hdu.Id);
        Assert.Equal((2, 1), (scienceCut.Image.Hdu.Index, weightCut.Image.Hdu.Index)); // matched by name, not by place
        Assert.Equal(scienceCut.Box, weightCut.Box);
        Assert.Equal(scienceCut.Axes, weightCut.Axes);
    }

    [Fact]
    public void AWeightMapWithoutOneOfTheCcds_IsGreyed_SayingWhich()
    {
        var science = Mosaic("mosaic.fits", -32, ["ccd00", "ccd01"], (ccd, x, y) => x);
        Mosaic("mosaic.weight.fits", -32, ["ccd00"], (ccd, x, y) => x);

        var companion = Assert.Single(LocalFitsFile.Inspect(science, "cadc:CFHT/mosaic.fits", ["cadc:CFHT/mosaic.weight.fits"]).Companions);

        Assert.Contains("[ccd01]", companion.Unavailable);
    }

    /// <summary>Two 50 × 40 CCDs, side by side on the sky, each named, after a primary header of no data.</summary>
    private string Mosaic(string name, int bitpix, string[] ccds, Func<int, int, int, double> value)
    {
        var hdus = new List<byte[]> { Hdu([Card("SIMPLE", "T"), Card("BITPIX", 8L), Card("NAXIS", 0L), Card("EXTEND", "T")]) };
        foreach (var ccd in ccds)
        {
            var n = int.Parse(ccd[3..]);
            var cards = ImageCards(bitpix, primary: false, 50, 40);
            cards.Add(Text("EXTNAME", ccd));
            cards.AddRange(TanWcs(25.5 - n * 60, 20.5, 150.0, 2.2, Scale)); // CCD n lies 60 pixels east of CCD n−1
            hdus.Add(Hdu(cards, Pixels(bitpix, 50, 40, (x, y, _) => value(n, x, y))));
        }
        return Write(_dir, name, [.. hdus]);
    }

    // ── What the rules say ───────────────────────────────────────────────────

    [Fact]
    public void AskingForAFileNotBesideIt_IsRefused_ByName()
    {
        var science = Science();
        Weight();
        var source = Source(science, WeightId, "cadc:CFHTSG/tile.R.flag.fits");

        var check = source.Check(CircleAt(source, 20, 15, 6, "cadc:CFHTSG/tile.R.flag.fits"));

        Assert.Contains("tile.R.flag.fits is not beside this file", Assert.Single(check.Errors));
    }

    [Fact]
    public void AskingForAGreyedCompanion_IsRefused_WithWhy()
    {
        var science = Science();
        Weight(shift: 3);
        var source = Source(science, WeightId);

        var check = source.Check(CircleAt(source, 20, 15, 6, WeightId));

        Assert.Contains("tile.R.weight.fits cannot be cut with this file: Its image [0] is not on the same pixels", Assert.Single(check.Errors));
    }

    /// <summary>A file with none beside it cannot take one — and is told where one would have to be.</summary>
    [Fact]
    public void AFileWithNoCompanionsBesideIt_CannotTakeAny()
    {
        var source = Source(Science(), WeightId);

        var check = source.Check(CircleAt(source, 20, 15, 6, WeightId));

        Assert.Contains("None of the observation's other files can be cut with this one", Assert.Single(check.Errors));
    }

    // ── Made, recorded and removed together ──────────────────────────────────

    /// <summary>
    /// The whole way through: the cutout and its weight map written together, each with true checksums,
    /// their sizes exactly the estimate; kept in Research as one cutout; and removed together.
    /// </summary>
    [Fact]
    public async Task TheCutoutAndItsWeightMap_AreMadeRecordedAndRemovedTogether()
    {
        var science = Science();
        Weight();
        var store = new ObservationStore();
        store.Save(new DownloadedObservation { PublisherID = "ivo://cadc/CFHTSG", LocalPath = science, ArtifactId = ScienceId });
        var downloader = new ObservationDownloader(() => throw new InvalidOperationException("a local cut needs no network"), store,
            [new LocalCutoutMaker((pid, artifact) => LocalCopies.Find(store.Observations, pid, artifact)?.LocalPath)]);
        var source = CutoutSources.Local(store.Observations, "ivo://cadc/CFHTSG", [ScienceId, WeightId])!;
        var spec = CircleAt(source, 40, 25, 8, WeightId);
        var target = Path.Combine(_dir, "m31.fits");

        await downloader.Start(new ObservationDownloadRequest("ivo://cadc/CFHTSG", target,
            new DownloadedObservation { PublisherID = "ivo://cadc/CFHTSG", Cutout = spec }));

        var record = Assert.Single(store.Observations, o => o.IsCutout);
        var weightCut = Assert.Single(record.Cutout!.CompanionPaths(target));
        Assert.Equal(new[] { target, weightCut }, record.LocalFiles);
        Assert.Equal(source.EstimateBytes(spec), new FileInfo(target).Length + new FileInfo(weightCut).Length);
        foreach (var path in record.LocalFiles)
        {
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(0xFFFFFFFFu, FitsChecksum.Sum(bytes));
        }

        Assert.Null(ResearchRecords.RemoveLocalFile(store, record));
        Assert.False(File.Exists(target));
        Assert.False(File.Exists(weightCut));
        Assert.True(File.Exists(science));
    }

    /// <summary>A cutout that would be saved over its companion's own file is refused, and nothing is written.</summary>
    [Fact]
    public async Task ACutoutSavedOverItsCompanionsFile_IsRefused()
    {
        var science = Science();
        var weight = Weight();
        var source = Source(science, WeightId);
        var spec = CircleAt(source, 20, 15, 6, WeightId);
        var before = File.ReadAllBytes(weight);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Make(source, spec, weight));

        Assert.Contains("over the file it is cut from", refused.Message);
        Assert.Equal(before, File.ReadAllBytes(weight));
    }

    // ── Written as one ───────────────────────────────────────────────────────

    /// <summary>None of the files is put in place unless every one is written: a failure leaves each as it was.</summary>
    [Fact]
    public void SeveralFilesWrittenAsOne_AreAllOrNothing()
    {
        var (a, b) = (Path.Combine(_dir, "a.fits"), Path.Combine(_dir, "b.fits"));
        File.WriteAllText(a, "before");

        Assert.Throws<IOException>(() => AtomicFile.WriteStreams([
            (a, s => s.Write("after"u8)),
            (b, _ => throw new IOException("the disk is full")),
        ]));

        Assert.Equal("before", File.ReadAllText(a));
        Assert.False(File.Exists(b));
        Assert.Equal(new[] { "a.fits" }, Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    [Fact]
    public void SeveralFilesWrittenAsOne_AreAllWritten()
    {
        var (a, b) = (Path.Combine(_dir, "a.fits"), Path.Combine(_dir, "b.fits"));
        File.WriteAllText(a, "before");

        AtomicFile.WriteStreams([(a, s => s.Write("A"u8)), (b, s => s.Write("B"u8))]);

        Assert.Equal(("A", "B"), (File.ReadAllText(a), File.ReadAllText(b)));
    }
}
