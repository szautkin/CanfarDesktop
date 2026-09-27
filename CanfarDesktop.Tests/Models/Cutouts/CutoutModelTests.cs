using System.Globalization;
using System.Text.Json;
using Xunit;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Models.Cutouts;
using CanfarDesktop.Services;

namespace CanfarDesktop.Tests.Models.Cutouts;

/// <summary>A region and a cutout: what the editor edits, what is saved, and what goes to SODA.</summary>
public class CutoutModelTests
{
    // ── SkyRegion ────────────────────────────────────────────────────────────

    /// <summary>SODA's numbers are the invariant culture's, whatever the person's: a French comma is not a decimal point to CADC.</summary>
    [Fact]
    public void TheSodaValue_UsesAPoint_EvenInFrench()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
            Assert.Equal("10.68 41.27 0.05", SkyRegion.Circle(10.68, 41.27, 0.05).ToSoda());
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    /// <summary>A box is a true rectangle on the sky, at any Dec — not a rectangle in RA and Dec.</summary>
    [Fact]
    public void ABox_IsItsSizeOnTheSky_AtHighDec()
    {
        var corners = SkyRegion.Box(10, 60, 1, 0.5).Outline();

        Assert.Equal(4, corners.Count);
        Assert.Equal(1.0, SkyGeometry.Distance(corners[0], corners[1]), 2); // south edge, along RA
        Assert.Equal(0.5, SkyGeometry.Distance(corners[1], corners[2]), 2); // west edge, along Dec
        Assert.True(SkyGeometry.SignedArea(corners) < 0);                   // wound as CADC winds
    }

    [Theory]
    [InlineData(10, 41, 0, RegionProblem.SizeNotPositive)]
    [InlineData(10, 95, 0.1, RegionProblem.DecOutOfRange)]
    [InlineData(10, 41, 100, RegionProblem.TooLarge)]
    [InlineData(double.NaN, 41, 0.1, RegionProblem.NotANumber)]
    public void AnUnsendableCircle_SaysWhy(double ra, double dec, double r, RegionProblem problem)
        => Assert.Equal(problem, SkyRegion.Circle(ra, dec, r).Problem());

    [Fact]
    public void APolygon_NeedsThreeCorners()
        => Assert.Equal(RegionProblem.TooFewVertices, SkyRegion.Polygon([new(10, 41), new(11, 41)]).Problem());

    [Theory]
    [InlineData(0.001, "3.6″")]
    [InlineData(0.05, "3′")]
    [InlineData(2.0, "2°")]
    public void AnAngle_ReadsInTheUnitThatSuitsIt(double degrees, string expected)
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Assert.Equal(expected, SkyRegion.Angle(degrees));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    // ── CutoutSpec ───────────────────────────────────────────────────────────

    private static CutoutSpec Spec(double r = 0.05) => new()
    {
        ArtifactId = "cadc:CFHTSG/x.fits",
        Region = SkyRegion.Circle(10.68, 41.27, r),
    };

    [Fact]
    public void TheKey_IsTheSameForTheSameCutout_AndDiffersForAnother()
    {
        Assert.Equal(Spec().Key, Spec().Key);
        Assert.NotEqual(Spec(0.05).Key, Spec(0.06).Key);
        Assert.Equal(8, Spec().Key.Length);
    }

    /// <summary>
    /// A SODA cutout's key is what it was before a cutout could be cut locally: records saved then, and
    /// the files named after them, keep their names. Pinned against the hash of the canonical form.
    /// </summary>
    [Fact]
    public void ASodaCutoutsKey_IsTheOneItAlwaysHad()
    {
        Assert.Equal(CutoutMethod.Soda, Spec().CutBy);
        Assert.Equal("d63c8c7a", Spec().Key);
        Assert.Equal("x.cutout-d63c8c7a.fits", Spec().FileName);
    }

    /// <summary>The same region cut locally is another product: it must not replace CADC's cut in Research, nor it that.</summary>
    [Fact]
    public void ALocalCut_IsADifferentProduct_FromCadcsCutOfTheSameRegion()
    {
        var local = Spec() with { CutBy = CutoutMethod.Local };

        Assert.NotEqual(Spec().Key, local.Key);
        Assert.Equal(8, local.Key.Length);
        Assert.Equal(Spec().Summary, local.Summary); // the same region, said the same way
    }

    /// <summary>Choosing images makes another product, and says which; choosing none is every image, as before.</summary>
    [Fact]
    public void ChosenImages_AreACutoutOfTheirOwn()
    {
        var chosen = Spec() with { CutBy = CutoutMethod.Local, Extensions = ["SCI,1", "ERR,1"] };

        Assert.NotEqual((Spec() with { CutBy = CutoutMethod.Local }).Key, chosen.Key);
        Assert.Equal("d63c8c7a", (Spec() with { Extensions = [] }).Key);
        Assert.EndsWith(" · [SCI,1] [ERR,1]", chosen.Summary);
    }

    // ── The observation a cutout was cut from ─────────────────────────────────

    private const string Tile = "ivo://cadc.nrc.ca/CFHTMEGAPIPE?G006.010.684+41.269/G006.010.684+41.269.I";

    /// <summary>
    /// A cutout's complete observation is the record of the same observation that is not a cutout —
    /// kept with its file or without — whichever order Research holds them in; not another observation's.
    /// </summary>
    [Fact]
    public void ACutoutsOriginal_IsTheCompleteRecordOfTheSameObservation()
    {
        var cutout = new DownloadedObservation { PublisherID = Tile, Cutout = Spec() };
        var another = new DownloadedObservation { PublisherID = Tile, Cutout = Spec(0.2) };
        var whole = new DownloadedObservation { PublisherID = Tile }; // kept without its file
        var elsewhere = new DownloadedObservation { PublisherID = "ivo://cadc.nrc.ca/CFHT?22803/22803p" };

        Assert.Same(whole, ResearchRecords.Complete([cutout, elsewhere, another, whole], Tile));
        Assert.Null(ResearchRecords.Complete([cutout, another, elsewhere], Tile)); // only cutouts of it: Search, then
        Assert.Null(CanfarDesktop.Services.Cutouts.Local.LocalCopies.CompleteFile([cutout, whole], Tile)); // no file here to cut from
    }

    /// <summary>Search finds an observation by the archive's id: the record's own, else the one its publisher ID names.</summary>
    [Fact]
    public void TheIdSearchFindsAnObservationBy_IsTheRecordsOrItsPublisherIds()
    {
        Assert.Equal("1234567", ResearchRecords.ObservationIdOf(new DownloadedObservation { PublisherID = Tile, ObservationID = "1234567" }));
        Assert.Equal("G006.010.684+41.269", ResearchRecords.ObservationIdOf(new DownloadedObservation { PublisherID = Tile }));
        Assert.Equal("", ResearchRecords.ObservationIdOf(new DownloadedObservation { PublisherID = "not a publisher id" }));
    }

    /// <summary>
    /// A cutout with its weight map is the same cutout: the same key, the same file; each companion's
    /// file is named by that key, beside it, so a record finds them again from itself.
    /// </summary>
    [Fact]
    public void Companions_KeepTheCutoutsKey_AndAreNamedByIt()
    {
        var alone = Spec() with { CutBy = CutoutMethod.Local };
        var paired = alone with { Companions = ["cadc:CFHTSG/x.weight.fits.fz", "cadc:CFHTSG/x.weight.fits.fz"] };
        var cutout = Path.Combine("data", "m31.fits");

        Assert.Equal(alone.Key, paired.Key);
        Assert.Equal(Path.Combine("data", $"x.weight.cutout-{alone.Key}.fits"), paired.CompanionPath(cutout, "cadc:CFHTSG/x.weight.fits.fz"));
        Assert.Single(paired.CompanionPaths(cutout)); // once, however often it was named
        Assert.EndsWith(" · + x.weight.fits.fz, x.weight.fits.fz", paired.Summary);
        Assert.Empty(alone.CompanionPaths(cutout));
    }

    /// <summary>A record's files: its own, and its companions' when it is a cutout that took some; none when it has no file.</summary>
    [Fact]
    public void ARecordsFiles_AreItsOwnAndItsCompanions()
    {
        var spec = Spec() with { CutBy = CutoutMethod.Local, Companions = ["cadc:CFHTSG/x.weight.fits"] };
        var cutout = Path.Combine("data", "m31.fits");

        Assert.Equal(new[] { cutout, Path.Combine("data", $"x.weight.cutout-{spec.Key}.fits") },
            new DownloadedObservation { LocalPath = cutout, Cutout = spec }.LocalFiles);
        Assert.Equal(new[] { cutout }, new DownloadedObservation { LocalPath = cutout }.LocalFiles);
        Assert.Empty(new DownloadedObservation { Cutout = spec }.LocalFiles);
    }

    /// <summary>
    /// Who cut it is saved by name, and a record saved before there was a choice reads as CADC's —
    /// which is what every one of them was.
    /// </summary>
    [Fact]
    public void WhoCutIt_IsSavedByName_AndDefaultsToCadc()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var json = JsonSerializer.Serialize(Spec() with { CutBy = CutoutMethod.Local }, options);

        Assert.Contains("\"CutBy\":\"Local\"", json);
        Assert.Equal(CutoutMethod.Local, JsonSerializer.Deserialize<CutoutSpec>(json, options)!.CutBy);

        var before = JsonSerializer.Deserialize<CutoutSpec>(
            """{"ArtifactId":"cadc:CFHTSG/x.fits","Region":{"Shape":0,"Ra":10.68,"Dec":41.27,"Radius":0.05}}""", options)!;
        Assert.Equal(CutoutMethod.Soda, before.CutBy);
        Assert.Equal(Spec().Key, before.Key);
        Assert.Empty(before.Companions);

        var paired = Spec() with { CutBy = CutoutMethod.Local, Companions = ["cadc:CFHTSG/x.weight.fits"] };
        Assert.Equal(paired.Companions, JsonSerializer.Deserialize<CutoutSpec>(JsonSerializer.Serialize(paired, options), options)!.Companions);
    }

    /// <summary>Saved and read back as Research saves it, it is still the same cutout.</summary>
    [Fact]
    public void ACutout_SurvivesBeingSaved()
    {
        var spec = new CutoutSpec
        {
            ArtifactId = "cadc:JCMT/cube.fits",
            Region = SkyRegion.Box(10.6, 41.3, 0.02, 0.01),
            BandMin = 8.66e-4,
            BandMax = 8.67e-4,
        };
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true };

        var back = JsonSerializer.Deserialize<CutoutSpec>(JsonSerializer.Serialize(spec, options), options)!;

        Assert.Equal(spec.Key, back.Key);
        Assert.Equal(SkyShape.Box, back.Region!.Shape);
        Assert.Equal(0.02, back.Region.Width);
    }

    [Fact]
    public void TheSummary_SaysTheRegionAndTheBand()
    {
        var summary = (Spec() with { BandMin = 8.66e-4, BandMax = 8.67e-4 }).Summary;

        Assert.StartsWith("r ", summary);
        Assert.Contains("µm", summary);
    }

    // ── The Research record ──────────────────────────────────────────────────

    /// <summary>
    /// The store used to replace by publisher id alone, so a cutout would have overwritten the complete
    /// download it was cut from. Now they sit side by side, and the same cutout again replaces itself.
    /// </summary>
    [Fact]
    public void ACutout_AndTheCompleteObservation_AreSeparateRecords()
    {
        var store = new ObservationStore(); // in memory without a packaged app
        store.Save(new DownloadedObservation { PublisherID = "ivo://cadc/A", LocalPath = "full.fits" });
        store.Save(new DownloadedObservation { PublisherID = "ivo://cadc/A", LocalPath = "cut1.fits", Cutout = Spec() });
        store.Save(new DownloadedObservation { PublisherID = "ivo://cadc/A", LocalPath = "cut1-again.fits", Cutout = Spec() });
        store.Save(new DownloadedObservation { PublisherID = "ivo://cadc/A", LocalPath = "cut2.fits", Cutout = Spec(0.1) });

        Assert.Equal(3, store.Observations.Count);
        Assert.Contains(store.Observations, o => o.LocalPath == "cut1-again.fits");
        Assert.DoesNotContain(store.Observations, o => o.LocalPath == "cut1.fits");
    }

    /// <summary>Asked for the observation, the complete one answers; its local id still names a cutout exactly.</summary>
    [Fact]
    public void FindingAnObservation_FindsTheCompleteOne()
    {
        var store = new ObservationStore();
        var cut = new DownloadedObservation { PublisherID = "ivo://cadc/A", Cutout = Spec() };
        store.Save(cut);
        store.Save(new DownloadedObservation { PublisherID = "ivo://cadc/A", LocalPath = "full.fits" });

        Assert.False(store.Find("ivo://cadc/A")!.IsCutout);
        Assert.True(store.Find(cut.Id)!.IsCutout);
    }

    /// <summary>
    /// Saving an observation without its file never overwrites one that has it — which is what the
    /// Search page's unused copy of this would have done, forgetting the downloaded file.
    /// </summary>
    [Fact]
    public void SavingWithoutAFile_LeavesADownloadedRecordAlone()
    {
        var store = new ObservationStore();
        store.Save(new DownloadedObservation { PublisherID = "ivo://cadc/A", LocalPath = "full.fits" });

        Assert.False(store.SaveIfAbsent(new DownloadedObservation { PublisherID = "ivo://cadc/A" }));
        Assert.Equal("full.fits", Assert.Single(store.Observations).LocalPath);

        Assert.True(store.SaveIfAbsent(new DownloadedObservation { PublisherID = "ivo://cadc/B" }));
        Assert.True(store.Has("ivo://cadc/B"));
        Assert.False(store.Has("ivo://cadc/B", Spec().Key)); // no cutout of it
    }

    /// <summary>The file goes; the observation, and for a cutout its region, stay to be fetched again.</summary>
    [Fact]
    public void RemovingTheFile_KeepsTheRecord_AndItsCutout()
    {
        var path = Path.Combine(Path.GetTempPath(), $"verbinal-remove-{Guid.NewGuid():N}.fits");
        File.WriteAllText(path, "pixels");
        var store = new ObservationStore();
        var record = new DownloadedObservation { PublisherID = "ivo://cadc/A", LocalPath = path, FileSize = 6, Cutout = Spec() };
        store.Save(record);

        Assert.Null(ResearchRecords.RemoveLocalFile(store, record));

        Assert.False(File.Exists(path));
        var kept = Assert.Single(store.Observations);
        Assert.Equal((string.Empty, (long?)null), (kept.LocalPath, kept.FileSize));
        Assert.Equal(Spec().Key, kept.Cutout!.Key);
    }

    /// <summary>A file held open elsewhere is not deleted, and the record still points at it — the reason is given.</summary>
    [Fact]
    public void AFileHeldOpen_IsNotForgotten()
    {
        var path = Path.Combine(Path.GetTempPath(), $"verbinal-held-{Guid.NewGuid():N}.fits");
        File.WriteAllText(path, "pixels");
        try
        {
            var store = new ObservationStore();
            var record = new DownloadedObservation { PublisherID = "ivo://cadc/A", LocalPath = path };
            store.Save(record);

            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.NotNull(ResearchRecords.RemoveLocalFile(store, record));

            Assert.Equal(path, store.Observations[0].LocalPath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A footprint across RA 0° is centred there, not on the far side of the sky.</summary>
    [Fact]
    public void AResearchRecord_IsCentredOnItsFootprint_EvenAcrossRaZero()
    {
        var caom2 = new CanfarDesktop.Models.Caom2.CAOM2Observation
        {
            Planes =
            [
                new CanfarDesktop.Models.Caom2.Caom2Plane
                {
                    Position = new CanfarDesktop.Models.Caom2.Caom2Position
                    {
                        Polygon = [new(359.9, 10), new(0.1, 10), new(0.1, 10.2), new(359.9, 10.2)],
                    },
                },
            ],
        };

        var record = ResearchRecords.ForObservation("ivo://cadc/A", caom2, null);
        var ra = double.Parse(record.RA, CultureInfo.InvariantCulture);

        Assert.True(ra < 0.01 || ra > 359.99, $"centred at RA {ra}");
    }
}
