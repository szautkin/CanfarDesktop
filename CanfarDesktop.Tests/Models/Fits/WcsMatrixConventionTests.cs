using Xunit;
using CanfarDesktop.Models.Fits;

namespace CanfarDesktop.Tests.Models.Fits;

/// <summary>
/// The three ways a header can state its rotation/scale matrix, and the three ways this app counts
/// pixels.
///
/// Both halves were latent faults rather than hypotheticals: a JWST i2d header carries PC + CDELT and
/// neither of the forms <c>FromHeader</c> used to read, so its rotation silently became zero — no
/// error at the reference pixel, growing with distance from it.
/// </summary>
public class WcsMatrixConventionTests
{
    private const double Scale = 0.001;          // 3.6″/px
    private const double Rot = 30.0;             // degrees
    private static readonly double RotRad = Rot * Math.PI / 180.0;

    private static FitsHeader Base()
    {
        var h = new FitsHeader();
        h.Add(new FitsCard("CRPIX1", "512", ""));
        h.Add(new FitsCard("CRPIX2", "512", ""));
        h.Add(new FitsCard("CRVAL1", "180.0", ""));
        h.Add(new FitsCard("CRVAL2", "45.0", ""));
        h.Add(new FitsCard("CTYPE1", "RA---TAN", ""));
        h.Add(new FitsCard("CTYPE2", "DEC--TAN", ""));
        return h;
    }

    private static void AddCdelt(FitsHeader h)
    {
        h.Add(new FitsCard("CDELT1", (-Scale).ToString("R"), ""));
        h.Add(new FitsCard("CDELT2", Scale.ToString("R"), ""));
    }

    // ── The PC matrix ────────────────────────────────────────────────────────

    /// <summary>
    /// The regression. PC + CDELT and nothing else — what a JWST i2d header carries — used to fall
    /// through to the CROTA2 branch, where a missing CROTA2 reads as zero rotation.
    /// </summary>
    [Fact]
    public void FromHeader_ReadsPcMatrixTimesCdelt()
    {
        var h = Base();
        AddCdelt(h);
        h.Add(new FitsCard("PC1_1", Math.Cos(RotRad).ToString("R"), ""));
        h.Add(new FitsCard("PC1_2", (-Math.Sin(RotRad)).ToString("R"), ""));
        h.Add(new FitsCard("PC2_1", Math.Sin(RotRad).ToString("R"), ""));
        h.Add(new FitsCard("PC2_2", Math.Cos(RotRad).ToString("R"), ""));

        var wcs = WcsInfo.FromHeader(h);

        // CDi_j = CDELTi * PCi_j — the row's own CDELT, not the column's.
        Assert.Equal(-Scale * Math.Cos(RotRad), wcs.Cd1_1, 12);
        Assert.Equal(-Scale * -Math.Sin(RotRad), wcs.Cd1_2, 12);
        Assert.Equal(Scale * Math.Sin(RotRad), wcs.Cd2_1, 12);
        Assert.Equal(Scale * Math.Cos(RotRad), wcs.Cd2_2, 12);

        // And the thing that was actually wrong: the frame is rotated, not axis-aligned.
        Assert.True(Math.Abs(wcs.NorthAngle) > 1.0,
            $"a PC-rotated frame must not read as north-up; got {wcs.NorthAngle}°");
    }

    /// <summary>PC defaults to the identity, so one stated element must not zero the rest.</summary>
    [Fact]
    public void FromHeader_PartialPcMatrix_DefaultsToIdentity()
    {
        var h = Base();
        AddCdelt(h);
        h.Add(new FitsCard("PC1_2", "0.5", ""));

        var wcs = WcsInfo.FromHeader(h);

        Assert.Equal(-Scale * 1.0, wcs.Cd1_1, 12);   // PC1_1 defaults to 1
        Assert.Equal(-Scale * 0.5, wcs.Cd1_2, 12);
        Assert.Equal(0.0, wcs.Cd2_1, 12);            // PC2_1 defaults to 0
        Assert.Equal(Scale * 1.0, wcs.Cd2_2, 12);    // PC2_2 defaults to 1
    }

    /// <summary>CD wins when a header states both — the standard's precedence.</summary>
    [Fact]
    public void FromHeader_CdMatrixBeatsPc()
    {
        var h = Base();
        AddCdelt(h);
        h.Add(new FitsCard("PC1_1", "0.5", ""));
        h.Add(new FitsCard("PC2_2", "0.5", ""));
        h.Add(new FitsCard("CD1_1", (-Scale).ToString("R"), ""));
        h.Add(new FitsCard("CD1_2", "0", ""));
        h.Add(new FitsCard("CD2_1", "0", ""));
        h.Add(new FitsCard("CD2_2", Scale.ToString("R"), ""));

        var wcs = WcsInfo.FromHeader(h);

        Assert.Equal(-Scale, wcs.Cd1_1, 12);
        Assert.Equal(Scale, wcs.Cd2_2, 12);
    }

    /// <summary>No PC and no CD still means CDELT + CROTA2, unchanged.</summary>
    [Fact]
    public void FromHeader_NoPcNoCd_StillReadsCrota2()
    {
        var h = Base();
        AddCdelt(h);
        h.Add(new FitsCard("CROTA2", Rot.ToString("R"), ""));

        var wcs = WcsInfo.FromHeader(h);

        Assert.Equal(-Scale * Math.Cos(RotRad), wcs.Cd1_1, 12);
        Assert.Equal(-Scale * Math.Sin(RotRad), wcs.Cd1_2, 12);
    }

    /// <summary>
    /// The error the PC gap produced has a signature: nil at the reference pixel and growing with
    /// distance from it. Measured against the same header read as a CD matrix, which is the answer.
    /// </summary>
    [Fact]
    public void PcRotation_ErrorGrowsWithDistanceFromReferencePixel()
    {
        var pc = Base();
        AddCdelt(pc);
        pc.Add(new FitsCard("PC1_1", Math.Cos(RotRad).ToString("R"), ""));
        pc.Add(new FitsCard("PC1_2", (-Math.Sin(RotRad)).ToString("R"), ""));
        pc.Add(new FitsCard("PC2_1", Math.Sin(RotRad).ToString("R"), ""));
        pc.Add(new FitsCard("PC2_2", Math.Cos(RotRad).ToString("R"), ""));

        // What the CROTA2 fallback would have produced: no rotation at all.
        var unrotated = Base();
        AddCdelt(unrotated);

        var good = WcsInfo.FromHeader(pc);
        var bad = WcsInfo.FromHeader(unrotated);

        Assert.Equal(0.0, Separation(good, bad, 512, 512), 9);           // at CRPIX: nothing
        var near = Separation(good, bad, 612, 612);                       // 100 px out
        var far = Separation(good, bad, 2512, 2512);                      // 2000 px out
        Assert.True(far > near * 10, $"error must grow with distance; near {near}″, far {far}″");
        Assert.True(far > 40.0, $"a 30° rotation error 2000 px out is arcminutes, not {far}″");
    }

    /// <summary>Angular separation in arcseconds between what two solutions say about one pixel.</summary>
    private static double Separation(WcsInfo a, WcsInfo b, double px, double py)
    {
        var (ra1, dec1) = a.PixelToWorld(px, py);
        var (ra2, dec2) = b.PixelToWorld(px, py);
        var d1 = dec1 * Math.PI / 180.0;
        var d2 = dec2 * Math.PI / 180.0;
        var dRa = (ra2 - ra1) * Math.PI / 180.0;
        var cos = Math.Sin(d1) * Math.Sin(d2) + Math.Cos(d1) * Math.Cos(d2) * Math.Cos(dRa);
        return Math.Acos(Math.Clamp(cos, -1.0, 1.0)) * 180.0 / Math.PI * 3600.0;
    }

    // ── The pixel conventions ────────────────────────────────────────────────

    /// <summary>
    /// Every conversion cancels its inverse EXACTLY. This is the guarantee the hand-written copies
    /// could only offer for as long as each was edited alongside the others: a crosshair and a mark
    /// reading the same position must agree to the bit, not to within a rounding.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(511.5, 0.25)]
    [InlineData(2047, 4592)]
    public void PixelConversions_EachPairCancelsExactly(double x, double y)
    {
        const int Height = 4593;

        var (fx, fy) = PixelConvention.ArrayToFits(x, y);
        Assert.Equal((x, y), PixelConvention.FitsToArray(fx, fy));

        var (dx, dy) = PixelConvention.ArrayToDisplay(x, y, Height);
        Assert.Equal((x, y), PixelConvention.DisplayToArray(dx, dy, Height));

        var (gx, gy) = PixelConvention.DisplayToFits(x, y, Height);
        Assert.Equal((x, y), PixelConvention.FitsToDisplay(gx, gy, Height));
    }

    /// <summary>The conventions are what their names say, spelled out once so a change is visible.</summary>
    [Fact]
    public void PixelConversions_AreTheStatedConventions()
    {
        const int Height = 100;

        // FITS is 1-based: array (0,0) is FITS (1,1).
        Assert.Equal((1.0, 1.0), PixelConvention.ArrayToFits(0, 0));

        // Display row 0 is the LAST array row.
        Assert.Equal((0.0, 99.0), PixelConvention.DisplayToArray(0, 0, Height));
        Assert.Equal((0.0, 0.0), PixelConvention.DisplayToArray(0, 99, Height));

        // Composed: display (0,0) is the top-left, which is FITS (1, 100).
        Assert.Equal((1.0, 100.0), PixelConvention.DisplayToFits(0, 0, Height));
    }

    /// <summary>
    /// The sky round trip through the canvas convention closes on the pixel it started at — the pair
    /// the crosshair readout and the mark renderer each use one half of.
    /// </summary>
    [Fact]
    public void SkyAtDisplay_AndBack_ClosesOnTheSamePixel()
    {
        var h = Base();
        AddCdelt(h);
        h.Add(new FitsCard("PC1_1", Math.Cos(RotRad).ToString("R"), ""));
        h.Add(new FitsCard("PC1_2", (-Math.Sin(RotRad)).ToString("R"), ""));
        h.Add(new FitsCard("PC2_1", Math.Sin(RotRad).ToString("R"), ""));
        h.Add(new FitsCard("PC2_2", Math.Cos(RotRad).ToString("R"), ""));
        var wcs = WcsInfo.FromHeader(h);

        const int Height = 1024;
        foreach (var (x, y) in new[] { (0.0, 0.0), (511.0, 511.0), (1023.0, 1023.0), (37.25, 900.75) })
        {
            var (ra, dec) = PixelConvention.SkyAtDisplay(wcs, Height, x, y);
            var back = PixelConvention.DisplayOfSky(wcs, Height, ra, dec);

            Assert.NotNull(back);
            Assert.Equal(x, back!.Value.X, 9);
            Assert.Equal(y, back.Value.Y, 9);
        }
    }
}
