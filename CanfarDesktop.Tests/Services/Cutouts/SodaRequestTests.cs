using Xunit;
using CanfarDesktop.Models.Cutouts;
using CanfarDesktop.Services.Cutouts;
using static CanfarDesktop.Tests.Services.Cutouts.SodaDescriptorParserTests;

namespace CanfarDesktop.Tests.Services.Cutouts;

/// <summary>
/// The one judgement of a cutout: the editor shows these messages, an agent is refused with them, and
/// a URL is built only from a cutout that passed.
/// </summary>
public class SodaRequestTests
{
    private static CutoutSpec Circle(SodaDescriptor file, double ra, double dec, double r)
        => new() { ArtifactId = file.ArtifactId, Region = SkyRegion.Circle(ra, dec, r) };

    [Fact]
    public void ACircleInsideTheImage_Passes_AndBecomesTheSodaRequest()
    {
        var file = MegaPipe();
        var spec = Circle(file, 10.68, 41.27, 0.05);

        Assert.True(CutoutRules.Check(file, spec).IsValid);
        Assert.Equal(
            "https://ws.cadc-ccda.hia-iha.nrc-cnrc.gc.ca/caom2ops/sync?ID=cadc%3ACFHTSG%2FG006.010.684%2B41.269.R.fits&CIRCLE=10.68%2041.27%200.05",
            SodaRequest.Url(file, spec));
    }

    [Fact]
    public void ABox_GoesAsAPolygon()
    {
        var file = MegaPipe();
        var spec = new CutoutSpec { ArtifactId = file.ArtifactId, Region = SkyRegion.Box(10.68, 41.27, 0.1, 0.1) };

        Assert.Contains("&POLYGON=", SodaRequest.Url(file, spec));
    }

    [Fact]
    public void ARegionOffTheImage_IsRefused()
    {
        var file = MegaPipe();
        var check = CutoutRules.Check(file, Circle(file, 20, 41, 0.1));

        Assert.False(check.IsValid);
        Assert.Contains("outside", Assert.Single(check.Errors));
        Assert.Throws<InvalidOperationException>(() => SodaRequest.Url(file, Circle(file, 20, 41, 0.1)));
    }

    /// <summary>Straddling the edge is allowed — SODA trims it — but the person is told.</summary>
    [Fact]
    public void ARegionOverTheEdge_Passes_WithAWarning()
    {
        var file = MegaPipe();
        var check = CutoutRules.Check(file, Circle(file, 9.97, 41.8, 0.1));

        Assert.True(check.IsValid);
        Assert.Contains("trimmed", Assert.Single(check.Warnings));
    }

    [Fact]
    public void NothingToCut_IsRefused()
    {
        var file = MegaPipe();
        Assert.False(CutoutRules.Check(file, new CutoutSpec { ArtifactId = file.ArtifactId }).IsValid);
    }

    /// <summary>An image has no spectral axis: asking it for a band is refused, not silently ignored.</summary>
    [Fact]
    public void ABand_OnAnImageWithoutOne_IsRefused()
    {
        var file = MegaPipe();
        var spec = Circle(file, 10.68, 41.27, 0.05) with { BandMin = 5e-7, BandMax = 6e-7 };

        Assert.Contains("wavelength", Assert.Single(CutoutRules.Check(file, spec).Errors));
    }

    [Theory]
    [InlineData(8.66e-4, 8.67e-4, true, false)]  // within the cube's band
    [InlineData(8.66e-4, 8.70e-4, true, true)]   // over its top: trimmed
    [InlineData(1e-3, 2e-3, false, false)]       // beyond it
    [InlineData(8.67e-4, 8.66e-4, false, false)] // backwards
    public void ABand_OnTheCube_IsJudgedAgainstItsRange(double min, double max, bool valid, bool warned)
    {
        var file = JcmtCube();
        var check = CutoutRules.Check(file, new CutoutSpec { ArtifactId = file.ArtifactId, BandMin = min, BandMax = max });

        Assert.Equal(valid, check.IsValid);
        Assert.Equal(warned, check.Warnings.Count > 0);
    }

    [Fact]
    public void TheBand_IsSentInMetres_WithAnOpenEndAsInfinity()
    {
        var file = JcmtCube();
        var url = SodaRequest.Url(file, new CutoutSpec { ArtifactId = file.ArtifactId, BandMin = 8.66e-4 });

        Assert.EndsWith("&BAND=0.000866%20%2BInf", url);
    }

    /// <summary>
    /// "About 15 MB of 1.6 GB". Checked against CADC itself: this exact cutout came back as 15,024,960
    /// bytes — the square around the circle, which is what SODA returns, not the circle's own area.
    /// </summary>
    [Fact]
    public void TheEstimate_IsTheShareOfTheFileSodaReturns()
    {
        var file = MegaPipe();
        const long whole = 1_663_807_680;

        var bytes = SodaRequest.EstimateBytes(file, Circle(file, 10.68, 41.27, 0.05), whole)!.Value;

        Assert.InRange(bytes, 13_500_000, 16_500_000); // measured: 15,024,960
        Assert.Null(SodaRequest.EstimateBytes(file, Circle(file, 10.68, 41.27, 0.05), null));
    }

    /// <summary>The same messages reach the editor translated, and an agent in English.</summary>
    [Fact]
    public void Messages_GoThroughTheTranslation_WhenOneIsSet()
    {
        var file = MegaPipe();
        try
        {
            CutoutRules.Translate = key => key == "Cutout_CheckOutside" ? "hors champ" : null;
            Assert.Equal("hors champ", Assert.Single(CutoutRules.Check(file, Circle(file, 20, 41, 0.1)).Errors));
        }
        finally
        {
            CutoutRules.Translate = null;
        }
    }
}
