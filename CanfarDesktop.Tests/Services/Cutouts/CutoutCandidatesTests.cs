using Xunit;
using CanfarDesktop.Services.Cutouts;

namespace CanfarDesktop.Tests.Services.Cutouts;

/// <summary>"Cutout…" belongs on FITS files only — a preview JPEG has no region to cut.</summary>
public class CutoutCandidatesTests
{
    [Theory]
    [InlineData("application/fits", "cadc:CFHTSG/G006.010.684+41.269.R.fits", null)]
    [InlineData(null, "cadc:CFHTSG/G006.010.684+41.269.R.weight.fits.fz", null)]
    [InlineData("", "mast:HST/product/ib7711ndq_flt.fits", "science")]
    [InlineData("image/fits", "cadc:X/cube", null)]
    public void FitsFiles_AreCandidates(string? contentType, string uri, string? productType)
        => Assert.True(CutoutCandidates.IsFitsFile(contentType, uri, productType));

    [Theory]
    [InlineData("image/jpeg", "mast:HST/product/ib7711ndq_flt.jpg", "preview")]
    [InlineData("image/jpeg", "mast:HST/product/ib7711ndq_flt_thumb.jpg", "thumbnail")]
    [InlineData("text/plain", "cadc:CFHTSG/G006.010.684+41.269.R.cat", null)]
    [InlineData("application/x-tar", "https://ws.cadc/caom2ops/pkg?ID=x.fits", null)]
    [InlineData("application/fits", "cadc:X/preview.fits", "preview")] // a preview, whatever its format
    public void EverythingElse_IsNot(string? contentType, string uri, string? productType)
        => Assert.False(CutoutCandidates.IsFitsFile(contentType, uri, productType));

    // ── What a chosen file is cut as ─────────────────────────────────────────

    private static CanfarDesktop.Models.Cutouts.SodaDescriptor Cuttable(string artifactId)
        => SodaDescriptorParserTests.MegaPipe() with { ArtifactId = artifactId };

    /// <summary>
    /// A file chosen by name is cut only if CADC cuts THAT file: the weight map picked from the list is
    /// downloaded whole, never as a cutout of the science image, however few files CADC cuts.
    /// </summary>
    [Fact]
    public void AChosenFile_IsCutOnlyIfCadcCutsThatFile()
    {
        var science = Cuttable("cadc:CFHTSG/G006.010.684+41.269.I.fits.fz");

        Assert.Same(science, CutoutCandidates.CutFor([science], "G006.010.684+41.269.I.fits.fz"));
        Assert.Same(science, CutoutCandidates.CutFor([science], "g006.010.684+41.269.i.FITS.fz")); // names, however written
        Assert.Null(CutoutCandidates.CutFor([science], "G006.010.684+41.269.I.weight.fits.fz"));
        Assert.Null(CutoutCandidates.CutFor([science], "G006.010.684+41.269.I.jpg"));
    }

    /// <summary>With no file chosen by name, the one file CADC cuts — and none, when it cuts several.</summary>
    [Fact]
    public void NoFileChosen_IsTheOneCadcCuts()
    {
        var (a, b) = (Cuttable("cadc:X/a.fits"), Cuttable("cadc:X/b.fits"));

        Assert.Same(a, CutoutCandidates.CutFor([a], ""));
        Assert.Null(CutoutCandidates.CutFor([a, b], null));
        Assert.Null(CutoutCandidates.CutFor([], ""));
    }
}
