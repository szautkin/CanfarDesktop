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
}
