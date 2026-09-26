using Xunit;
using CanfarDesktop.Models.Cutouts;
using CanfarDesktop.Services.Cutouts;
using static CanfarDesktop.Tests.Services.Cutouts.SodaDescriptorParserTests;

namespace CanfarDesktop.Tests.Services.Cutouts;

/// <summary>The cutout an editor opens on: the search's target when it is on this file, else the middle of it.</summary>
public class CutoutPrefillTests
{
    [Fact]
    public void TheSearchTarget_OnThisFile_IsWhereTheCutoutStarts_AtTheSearchRadius()
    {
        var spec = CutoutPrefill.Suggest(MegaPipe(), new CutoutHints(Ra: 10.68, Dec: 41.27, RadiusDeg: 0.0167));

        Assert.Equal(SkyShape.Circle, spec.Region!.Shape);
        Assert.Equal((10.68, 41.27, 0.0167), (spec.Region.Ra, spec.Region.Dec, spec.Region.Radius));
        Assert.Equal("cadc:CFHTSG/G006.010.684+41.269.R.fits", spec.ArtifactId);
    }

    /// <summary>A target on another part of the sky says nothing about this file.</summary>
    [Fact]
    public void ATargetOffThisFile_IsIgnored_ForTheMiddleOfIt()
    {
        var file = MegaPipe();
        var spec = CutoutPrefill.Suggest(file, new CutoutHints(Ra: 20, Dec: 41));

        Assert.Equal(file.BoundingCircle!.Ra, spec.Region!.Ra, 9);
        Assert.Equal(file.BoundingCircle.Radius / 4, spec.Region.Radius, 9);
    }

    [Fact]
    public void WithNoSearch_ItIsTheMiddleOfTheFile_AQuarterOfItAcross()
    {
        var file = MegaPipe();
        var spec = CutoutPrefill.Suggest(file);

        Assert.Equal(file.BoundingCircle!.Dec, spec.Region!.Dec, 9);
        Assert.True(SodaRequest.Check(file, spec).IsValid);
    }

    /// <summary>A pinpoint search radius still makes a cutout worth having.</summary>
    [Fact]
    public void ATinyRadius_IsRaisedToTheSmallestUseful()
    {
        var spec = CutoutPrefill.Suggest(MegaPipe(), new CutoutHints(Ra: 10.68, Dec: 41.27, RadiusDeg: 1e-6));
        Assert.Equal(CutoutPrefill.MinRadius, spec.Region!.Radius, 12);
    }

    // ── The search's own cutout boxes ────────────────────────────────────────

    /// <summary>"Spatial cutout", as CADC's search page means it: the search's circle, on this row's file.</summary>
    [Fact]
    public void SpatialCutout_CutsTheSearchCircle_FromAFileItTouches()
    {
        var spec = CutoutPrefill.FromSearchFlags(MegaPipe(), new CutoutHints(10.68, 41.27, 0.05), spatial: true, spectral: false);
        Assert.Equal(0.05, spec!.Region!.Radius);
    }

    /// <summary>With nothing to cut — box not ticked, circle elsewhere, no band on an image — the whole file is what was asked.</summary>
    [Theory]
    [InlineData(false, false, 10.68, 41.27)]
    [InlineData(true, false, 20.0, 41.0)]
    [InlineData(false, true, 10.68, 41.27)]
    public void WithNothingToCut_ItIsTheWholeFile(bool spatial, bool spectral, double ra, double dec)
        => Assert.Null(CutoutPrefill.FromSearchFlags(MegaPipe(), new CutoutHints(ra, dec, 0.05, 8.66e-4, 8.67e-4), spatial, spectral));

    [Fact]
    public void SpectralCutout_CutsTheSearchBand_FromACube()
    {
        var spec = CutoutPrefill.FromSearchFlags(JcmtCube(), new CutoutHints(BandMin: 8.66e-4, BandMax: 8.67e-4), spatial: false, spectral: true);
        Assert.Equal((8.66e-4, 8.67e-4), (spec!.BandMin!.Value, spec.BandMax!.Value));
        Assert.Null(spec.Region);
    }

    /// <summary>The hints are the query builder's own reading of the form, so a cutout starts where the search looked.</summary>
    [Fact]
    public void TheHints_AreWhatTheSearchSearched()
    {
        var hints = CutoutHints.From(new CanfarDesktop.Models.SearchFormState
        {
            Target = "10.68 41.27",
            SearchRadius = 0.05,
            WavelengthMin = "8.66e-4",
            WavelengthMax = "8.67e-4",
        })!;

        Assert.Equal((10.68, 41.27, 0.05), (hints.Ra!.Value, hints.Dec!.Value, hints.RadiusDeg!.Value));
        Assert.Equal((8.66e-4, 8.67e-4), (hints.BandMin!.Value, hints.BandMax!.Value));
        Assert.Null(CutoutHints.From(new CanfarDesktop.Models.SearchFormState()));
    }

    /// <summary>The search's wavelengths, kept to what the cube has; an image ignores them.</summary>
    [Fact]
    public void TheSearchBand_IsKeptToTheFilesOwn_AndOnlyWhereThereIsOne()
    {
        var hints = new CutoutHints(BandMin: 8.66e-4, BandMax: 1e-3);

        var cube = CutoutPrefill.Suggest(JcmtCube(), hints);
        Assert.Equal(8.66e-4, cube.BandMin!.Value, 15);
        Assert.Equal(JcmtCube().BandMax!.Value, cube.BandMax!.Value, 15);

        var image = CutoutPrefill.Suggest(MegaPipe(), hints);
        Assert.Null(image.BandMin);
        Assert.Null(image.BandMax);
    }
}
