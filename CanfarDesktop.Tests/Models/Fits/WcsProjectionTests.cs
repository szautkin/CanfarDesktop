using Xunit;
using CanfarDesktop.Models.Fits;

namespace CanfarDesktop.Tests.Models.Fits;

/// <summary>
/// Round-trip tests for the zenithal projection family (TAN/SIN/STG/ZEA) ported from
/// the macOS FITSWCSProjectionTests. Reference: Calabretta &amp; Greisen 2002, A&amp;A 395, 1077.
/// </summary>
public class WcsProjectionTests
{
    private static WcsInfo Make(string ctype1, string ctype2) => new()
    {
        CrPix1 = 0, CrPix2 = 0, CrVal1 = 0, CrVal2 = 0,
        Cd1_1 = 1, Cd1_2 = 0, Cd2_1 = 0, Cd2_2 = 1,
        CType1 = ctype1, CType2 = ctype2,
    };

    [Theory]
    [InlineData("RA---TAN", "DEC--TAN", WcsInfo.Projection.Tan)]
    [InlineData("RA---SIN", "DEC--SIN", WcsInfo.Projection.Sin)]
    [InlineData("RA---STG", "DEC--STG", WcsInfo.Projection.Stg)]
    [InlineData("RA---ZEA", "DEC--ZEA", WcsInfo.Projection.Zea)]
    [InlineData("RA---CAR", "DEC--CAR", WcsInfo.Projection.Linear)] // unknown code
    [InlineData("RA---TAN", "DEC--SIN", WcsInfo.Projection.Linear)] // mismatched axes
    [InlineData("", "", WcsInfo.Projection.Linear)]                 // empty CTYPE
    // Regression: a "-SIP" distortion suffix must not be mistaken for the projection — the
    // projection is the token AFTER the coordinate name. Taking the last token read every SIP
    // image (TESS FFIs) as Linear, off by many arcminutes across the field.
    [InlineData("RA---TAN-SIP", "DEC--TAN-SIP", WcsInfo.Projection.Tan)]
    [InlineData("RA---SIN-SIP", "DEC--SIN-SIP", WcsInfo.Projection.Sin)]
    public void ProjectionCodeParsing(string c1, string c2, WcsInfo.Projection expected)
        => Assert.Equal(expected, Make(c1, c2).Proj);

    [Fact]
    public void PixelToWorld_AppliesSipForwardDistortion()
    {
        // 1″/px TAN at CRPIX/CRVAL, plus a pure A_2_0 quadratic: f(u,v) = A_2_0·u².
        var a = new double[3, 3]; a[2, 0] = 1e-4;
        var b = new double[3, 3]; // g = 0
        var sip = new WcsInfo
        {
            CrPix1 = 100, CrPix2 = 100, CrVal1 = 10, CrVal2 = 20,
            Cd1_1 = 1.0 / 3600, Cd1_2 = 0, Cd2_1 = 0, Cd2_2 = 1.0 / 3600,
            CType1 = "RA---TAN-SIP", CType2 = "DEC--TAN-SIP", SipA = a, SipB = b,
        };
        var noSip = sip with { SipA = null, SipB = null };

        // u = 100 (px=200): forward SIP maps offset 100 → 100 + 1e-4·100² = 101.
        // So SIP@px=200 must equal the undistorted mapping at px=201 (both offset 101).
        var (raSip, decSip) = sip.PixelToWorld(200, 100);
        var (raRef, decRef) = noSip.PixelToWorld(201, 100);
        Assert.Equal(raRef, raSip, 9);
        Assert.Equal(decRef, decSip, 9);
        // …and it actually differs from the undistorted mapping at the same pixel.
        var (raPlain, _) = noSip.PixelToWorld(200, 100);
        Assert.NotEqual(raPlain, raSip);
    }

    /// <summary>End-to-end SIP+TAN against astropy gold values (all_pix2world, origin=1) for real
    /// TESS FFIs. Guarded on the local files (skips elsewhere) — the same pattern as the fpack test.</summary>
    [Theory]
    [InlineData(@"C:\Users\szaut\OneDrive\Documents\tess2025233124603-s0096-3-3-0293-s_ffic.fits",
        42.763231, -71.818999, 36.008492, -63.976789)]
    [InlineData(@"C:\Users\szaut\OneDrive\Documents\tess2018262165941-s0002-3-3-0121-s_ffic.fits",
        37.874313, -70.826946, 33.762986, -62.750826)]
    public void TessFfi_SipWcs_MatchesAstropyReference_WhenAvailable(
        string path, double centerRa, double centerDec, double cornerRa, double cornerDec)
    {
        if (!System.IO.File.Exists(path)) return;

        using var stream = System.IO.File.OpenRead(path);
        var hdus = CanfarDesktop.Services.Fits.FitsParser.Parse(stream);
        var wcs = System.Linq.Enumerable.FirstOrDefault(
            System.Linq.Enumerable.Select(hdus, h => h.ImageData?.Wcs),
            w => w is { IsValid: true });
        Assert.NotNull(wcs);
        Assert.Equal(WcsInfo.Projection.Tan, wcs!.Proj); // was misread as Linear
        Assert.NotNull(wcs.SipA);
        Assert.NotNull(wcs.SipAp);

        AssertRaDec(wcs, 1068, 1039, centerRa, centerDec, 1.0); // center
        AssertRaDec(wcs, 2086, 2028, cornerRa, cornerDec, 2.0); // corner (SIP ~16′)

        // WorldToPixel round-trip closes via the AP/BP inverse SIP.
        var back = wcs.WorldToPixel(cornerRa, cornerDec);
        Assert.NotNull(back);
        Assert.Equal(2086, back!.Value.Px, 0);
        Assert.Equal(2028, back.Value.Py, 0);
    }

    private static void AssertRaDec(WcsInfo wcs, double px, double py, double ra, double dec, double tolArcsec)
    {
        var (r, d) = wcs.PixelToWorld(px, py);
        var off = System.Math.Sqrt(
            System.Math.Pow((r - ra) * System.Math.Cos(dec * System.Math.PI / 180), 2) +
            System.Math.Pow(d - dec, 2)) * 3600;
        Assert.True(off < tolArcsec, $"({px},{py}) off by {off:F2}\" — got RA={r:F5} Dec={d:F5}");
    }

    [Theory]
    [InlineData(WcsInfo.Projection.Tan)]
    [InlineData(WcsInfo.Projection.Sin)]
    [InlineData(WcsInfo.Projection.Stg)]
    [InlineData(WcsInfo.Projection.Zea)]
    public void RoundTrip(WcsInfo.Projection proj)
    {
        const double crval1 = 180.0; // equator-ish RA
        const double crval2 = 30.0;  // away from the poles for inverse stability

        for (var dRa = -0.5; dRa <= 0.5 + 1e-9; dRa += 0.25)
        for (var dDec = -0.5; dDec <= 0.5 + 1e-9; dDec += 0.25)
        {
            var ra = crval1 + dRa / Math.Cos(crval2 * Math.PI / 180);
            var dec = crval2 + dDec;

            var plane = WcsInfo.Project(ra, dec, crval1, crval2, proj);
            Assert.NotNull(plane);

            var world = WcsInfo.Deproject(plane!.Value.Xi, plane.Value.Eta, crval1, crval2, proj);
            Assert.NotNull(world);

            Assert.Equal(ra, world!.Value.Ra, 9);
            Assert.Equal(dec, world.Value.Dec, 9);
        }
    }

    [Fact]
    public void Sin_RejectsOutOfHemisphere()
        => Assert.Null(WcsInfo.Deproject(60, 60, 0, 0, WcsInfo.Projection.Sin));

    [Fact]
    public void Zea_RejectsOutOfDomain()
        => Assert.Null(WcsInfo.Deproject(200, 200, 0, 0, WcsInfo.Projection.Zea));

    [Theory]
    [InlineData(WcsInfo.Projection.Tan)]
    [InlineData(WcsInfo.Projection.Sin)]
    [InlineData(WcsInfo.Projection.Stg)]
    [InlineData(WcsInfo.Projection.Zea)]
    public void ReferencePointMapsToOrigin(WcsInfo.Projection proj)
    {
        var plane = WcsInfo.Project(180, 30, 180, 30, proj);
        Assert.NotNull(plane);
        Assert.Equal(0, plane!.Value.Xi, 12);
        Assert.Equal(0, plane.Value.Eta, 12);
    }

    [Theory]
    [InlineData(WcsInfo.Projection.Tan)]
    [InlineData(WcsInfo.Projection.Sin)]
    [InlineData(WcsInfo.Projection.Stg)]
    [InlineData(WcsInfo.Projection.Zea)]
    public void OriginMapsToReferencePoint(WcsInfo.Projection proj)
    {
        var world = WcsInfo.Deproject(0, 0, 180, 30, proj);
        Assert.NotNull(world);
        Assert.Equal(180, world!.Value.Ra, 12);
        Assert.Equal(30, world.Value.Dec, 12);
    }

    [Fact]
    public void Linear_ProjectAndDeproject_ReturnNull()
    {
        Assert.Null(WcsInfo.Project(10, 10, 0, 0, WcsInfo.Projection.Linear));
        Assert.Null(WcsInfo.Deproject(10, 10, 0, 0, WcsInfo.Projection.Linear));
    }

    [Fact]
    public void PixelToWorld_TanDiffersFromLinear_OffCentre()
    {
        // A wide-field TAN image: off-centre, proper TAN must differ from naive linear.
        var tan = new WcsInfo
        {
            CrPix1 = 0, CrPix2 = 0, CrVal1 = 180, CrVal2 = 60,
            Cd1_1 = 0.01, Cd1_2 = 0, Cd2_1 = 0, Cd2_2 = 0.01,
            CType1 = "RA---TAN", CType2 = "DEC--TAN",
        };
        var (ra, dec) = tan.PixelToWorld(100, 100);

        // Round-trips with WorldToPixel under the same projection.
        var px = tan.WorldToPixel(ra, dec);
        Assert.NotNull(px);
        Assert.Equal(100.0, px!.Value.Px, 6);
        Assert.Equal(100.0, px.Value.Py, 6);

        // And it is NOT the same as the linear approximation far from the reference pixel.
        var linearRa = 180 + 0.01 * 100;
        Assert.True(Math.Abs(ra - linearRa) > 1e-6);
    }
}
