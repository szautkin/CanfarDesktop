using Xunit;
using CanfarDesktop.Models;
using CanfarDesktop.Helpers.Notebook;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>SCI-10: the seeded analysis notebook builds a valid, round-trippable .ipynb pre-loaded
/// with the observation's metadata + local path + an astropy load.</summary>
public class AnalysisNotebookBuilderTests
{
    private static DownloadedObservation Obs() => new()
    {
        PublisherID = "ivo://cadc.nrc.ca/JWST?jw01",
        Collection = "JWST",
        ObservationID = "jw01",
        TargetName = "NGC 1234",
        Instrument = "NIRCam",
        CalLevel = "2",
        LocalPath = @"C:\Users\me\Downloads\Verbinal\jw01.fits",
    };

    [Theory]
    [InlineData("image")]
    [InlineData("photometry")]
    [InlineData("cube")]
    [InlineData("")] // defaults to image
    public void Build_ProducesRoundTrippableNotebook_WithMetadataAndAstropyLoad(string template)
    {
        var doc = AnalysisNotebookBuilder.Build(Obs(), template);

        // Round-trips through the SAME serializer/parser the app uses to save/open notebooks.
        var parsed = NotebookParser.Parse(NotebookParser.Serialize(doc));

        Assert.True(parsed.Cells.Count >= 3);
        Assert.Equal("markdown", parsed.Cells[0].CellType);

        var all = string.Join("\n", parsed.Cells.Select(c => c.SourceText));
        Assert.Contains("ivo://cadc.nrc.ca/JWST?jw01", all);  // publisher id in the header
        Assert.Contains("jw01.fits", all);                    // local path in the load cell
        Assert.Contains("from astropy.io import fits", all);  // astropy load
        Assert.Contains("WCS", all);
    }

    [Fact]
    public void Build_CubeTemplate_UsesSpectralCube()
    {
        var all = string.Join("\n", AnalysisNotebookBuilder.Build(Obs(), "cube").Cells.Select(c => c.SourceText));
        Assert.Contains("SpectralCube", all);
        Assert.Contains("moment", all);
    }

    [Fact]
    public void Build_PhotometryTemplate_UsesPhotutils()
    {
        var all = string.Join("\n", AnalysisNotebookBuilder.Build(Obs(), "photometry").Cells.Select(c => c.SourceText));
        Assert.Contains("photutils", all);
        Assert.Contains("aperture_photometry", all);
    }
}
