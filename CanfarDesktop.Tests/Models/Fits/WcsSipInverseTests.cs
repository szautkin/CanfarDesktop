using Xunit;
using CanfarDesktop.Models.Fits;

namespace CanfarDesktop.Tests.Models.Fits;

/// <summary>
/// Sky → pixel on an image with SIP distortion. Pixel → sky applies the forward polynomial (A/B);
/// the way back needs its inverse, and a header may not carry one.
///
/// <para>HST's calibrated frames give A/B and no AP/BP. The way back then skipped the distortion
/// altogether, so Go To, marks pinned to the sky and sky-region figures landed up to 6.6 pixels
/// (0.26″) from where the image's own readout put the same position, worse toward the corners.</para>
/// </summary>
public class WcsSipInverseTests
{
    /// <summary>
    /// The WCS of HST WFC3 frame ib7711ndq (extension 1), as CADC serves it: TAN-SIP, fourth order,
    /// forward coefficients only, reference pixel far off the frame's left edge.
    /// </summary>
    private static WcsInfo Hst() => new()
    {
        CType1 = "RA---TAN-SIP", CType2 = "DEC--TAN-SIP",
        CrPix1 = -1021.0, CrPix2 = 1.0,
        CrVal1 = 10.590068591852, CrVal2 = 41.249261862019,
        Cd1_1 = -2.4695975072986E-06, Cd1_2 = -1.0800289097198E-05,
        Cd2_1 = -1.0726833845973E-05, Cd2_2 = 1.7198302917245E-06,
        SipA = Sip(new()
        {
            [(0, 2)] = -2.0834386964525e-08, [(0, 3)] = 8.80501654106841e-12, [(0, 4)] = 4.22633067515342e-15,
            [(1, 1)] = -2.8898063487853e-06, [(1, 2)] = 2.25727179583451e-11, [(1, 3)] = 1.5311897457834e-14,
            [(2, 0)] = 2.83349082496993e-06, [(2, 1)] = 1.54968621647347e-12, [(2, 2)] = 3.64126014376653e-15,
            [(3, 0)] = 1.29500252041203e-11, [(3, 1)] = -1.6415806202481e-15, [(4, 0)] = 4.08019314352419e-15,
        }),
        SipB = Sip(new()
        {
            [(0, 2)] = -2.8846489733693e-06, [(0, 3)] = 2.81006609880016e-11, [(0, 4)] = -2.3223252634729e-14,
            [(1, 1)] = 2.82685956998421e-06, [(1, 2)] = -9.1248554893281e-12, [(1, 3)] = -1.6240463008788e-14,
            [(2, 0)] = 3.76037915878784e-09, [(2, 1)] = 3.05994128592113e-12, [(2, 2)] = -8.3956831962986e-16,
            [(3, 0)] = 1.49165202752694e-11, [(3, 1)] = -3.4432092328839e-16, [(4, 0)] = -2.8530488054685e-15,
        }),
    };

    private static double[,] Sip(Dictionary<(int P, int Q), double> terms, int order = 4)
    {
        var c = new double[order + 1, order + 1];
        foreach (var ((p, q), v) in terms) c[p, q] = v;
        return c;
    }

    /// <summary>FITS pixels across the 1027 × 1024 frame: corners, centre, and the far corner from CRPIX.</summary>
    public static TheoryData<double, double> AcrossTheFrame => new()
    {
        { 1, 1 }, { 1027, 1 }, { 1, 1024 }, { 1027, 1024 }, { 514, 512 }, { 901, 875 },
    };

    [Theory]
    [MemberData(nameof(AcrossTheFrame))]
    public void WithOnlyTheForwardTermsAPositionComesBackToItsOwnPixel(double px, double py)
    {
        var wcs = Hst();
        var (ra, dec) = wcs.PixelToWorld(px, py);

        var back = wcs.WorldToPixel(ra, dec);

        Assert.NotNull(back);
        Assert.Equal(px, back.Value.Px, 3);
        Assert.Equal(py, back.Value.Py, 3);
    }

    /// <summary>
    /// AP/BP are themselves a fitted approximation. Taken on their own they leave the way back a
    /// fraction of a pixel out; used as the first guess and refined against A/B, they do not.
    /// </summary>
    [Theory]
    [MemberData(nameof(AcrossTheFrame))]
    public void AnApproximateInverseIsRefinedToTheForwardSolution(double px, double py)
    {
        var hst = Hst();
        var wcs = hst with { SipAp = Negated(hst.SipA!), SipBp = Negated(hst.SipB!) };   // first-order inverse
        var (ra, dec) = wcs.PixelToWorld(px, py);

        var back = wcs.WorldToPixel(ra, dec);

        Assert.NotNull(back);
        Assert.Equal(px, back.Value.Px, 3);
        Assert.Equal(py, back.Value.Py, 3);
    }

    /// <summary>A distortion too strong to invert gives no pixel rather than a wrong one.</summary>
    [Fact]
    public void ADistortionThatCannotBeInvertedPlacesNothing()
    {
        var wcs = Hst() with
        {
            SipA = Sip(new() { [(2, 0)] = 5e-2 }, order: 2),
            SipB = Sip(new() { [(0, 2)] = 5e-2 }, order: 2),
        };

        Assert.Null(wcs.WorldToPixel(wcs.CrVal1 + 0.05, wcs.CrVal2 + 0.05));
    }

    [Fact]
    public void WithoutDistortionNothingChanges()
    {
        var wcs = Hst() with { SipA = null, SipB = null };
        var (ra, dec) = wcs.PixelToWorld(700, 300);

        var back = wcs.WorldToPixel(ra, dec)!.Value;

        Assert.Equal(700, back.Px, 9);
        Assert.Equal(300, back.Py, 9);
    }

    private static double[,] Negated(double[,] c)
    {
        var n = (double[,])c.Clone();
        for (var p = 0; p < n.GetLength(0); p++)
            for (var q = 0; q < n.GetLength(1); q++)
                n[p, q] = -n[p, q];
        return n;
    }
}
