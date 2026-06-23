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
    public void ProjectionCodeParsing(string c1, string c2, WcsInfo.Projection expected)
        => Assert.Equal(expected, Make(c1, c2).Proj);

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
