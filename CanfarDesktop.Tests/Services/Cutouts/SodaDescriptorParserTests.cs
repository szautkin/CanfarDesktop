using Xunit;
using CanfarDesktop.Models.Cutouts;
using CanfarDesktop.Services;
using CanfarDesktop.Services.Cutouts;

namespace CanfarDesktop.Tests.Services.Cutouts;

/// <summary>
/// Against CADC's real DataLink answers, saved as they came: a MegaPipe image (CIRCLE, POLYGON) and a
/// JCMT cube (BAND as well). The app used to drop these descriptors, keeping only rows with a URL.
/// </summary>
public class SodaDescriptorParserTests
{
    internal static string Fixture(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "DataLink", name));

    internal static SodaDescriptor MegaPipe() => Assert.Single(SodaDescriptorParser.Parse(Fixture("megapipe-image.xml")));
    internal static SodaDescriptor JcmtCube() => Assert.Single(SodaDescriptorParser.Parse(Fixture("jcmt-cube.xml")));

    /// <summary>Only the synchronous service: the answer lists async beside it, and the app runs cutouts as downloads.</summary>
    [Fact]
    public void TheImage_HasOneSyncService_WithItsFileAndParameters()
    {
        var file = MegaPipe();

        Assert.Equal("https://ws.cadc-ccda.hia-iha.nrc-cnrc.gc.ca/caom2ops/sync", file.AccessUrl);
        Assert.Equal("cadc:CFHTSG/G006.010.684+41.269.R.fits", file.ArtifactId);
        Assert.Equal("G006.010.684+41.269.R.fits", file.FileName);
        Assert.Equal(new[] { "CIRCLE", "ID", "POLYGON", "POS" }, file.Parameters.Order());
        Assert.True(file.SupportsSky);
        Assert.False(file.Supports("BAND"));
    }

    [Fact]
    public void TheImage_KnowsItsFootprint_AndItsBoundingCircle()
    {
        var file = MegaPipe();

        Assert.Equal(SkyShape.Polygon, file.Footprint!.Shape);
        Assert.Equal(4, file.Footprint.Vertices.Count);
        Assert.Equal(SkyShape.Circle, file.BoundingCircle!.Shape);
        Assert.Equal(0.7448381342809741, file.BoundingCircle.Radius, 12);
        Assert.Equal(41.27238891, file.BoundingCircle.Dec, 8);
    }

    [Fact]
    public void TheCube_CanAlsoBeCutByWavelength_WithinItsBand()
    {
        var file = JcmtCube();

        Assert.True(file.Supports("BAND"));
        Assert.Equal(8.657629528947576E-4, file.BandMin!.Value, 15);
        Assert.Equal(8.680495123089463E-4, file.BandMax!.Value, 15);
    }

    /// <summary>The same rule as every DataLink URL: a service offered over plain http is not followed.</summary>
    [Fact]
    public void AServiceNotOnHttps_IsIgnored()
    {
        var xml = Fixture("megapipe-image.xml").Replace("https://ws.cadc-ccda.hia-iha.nrc-cnrc.gc.ca/caom2ops/sync\"", "http://ws.cadc-ccda.hia-iha.nrc-cnrc.gc.ca/caom2ops/sync\"");
        Assert.Empty(SodaDescriptorParser.Parse(xml));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<VOTABLE><RESOURCE")]
    [InlineData("<!DOCTYPE x [<!ENTITY e SYSTEM \"file:///c:/windows/win.ini\">]><VOTABLE>&e;</VOTABLE>")]
    public void ANonsenseOrHostileAnswer_GivesNothing_RatherThanThrowing(string xml)
        => Assert.Empty(SodaDescriptorParser.Parse(xml));

    /// <summary>Through DataLink's own parse, beside the rows it always read.</summary>
    [Fact]
    public void DataLink_NowCarriesTheCutoutService_BesideTheFiles()
    {
        var result = DataLinkService.ParseVOTable(Fixture("megapipe-image.xml"));

        Assert.Single(result.DirectFiles);
        Assert.Single(result.Cutouts);
        Assert.NotNull(result.CutoutFor("cadc:CFHTSG/G006.010.684+41.269.R.fits"));
        Assert.Null(result.CutoutFor("cadc:CFHTSG/G006.010.684+41.269.R.weight.fits.fz"));
    }
}
