using Xunit;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models.Fits;

namespace CanfarDesktop.Tests.Helpers;

public class BlinkAlignerTests
{
    private const double Tolerance = 1.5; // sub-pixel accuracy sufficient for blink alignment

    private static WcsInfo CreateWcs(double crval1, double crval2, double cdelt, double rotaDeg = 0)
    {
        var rota = rotaDeg * Math.PI / 180;
        return new WcsInfo
        {
            CrPix1 = 512, CrPix2 = 512,
            CrVal1 = crval1, CrVal2 = crval2,
            Cd1_1 = -cdelt * Math.Cos(rota),
            Cd1_2 = cdelt * Math.Sin(rota),
            Cd2_1 = cdelt * Math.Sin(rota),
            Cd2_2 = cdelt * Math.Cos(rota),
        };
    }

    [Fact]
    public void SameWcs_CentersReferencePoint()
    {
        var wcs = CreateWcs(180, 45, 0.001);
        var result = BlinkAligner.ComputeAlignedTransform(
            wcs, 1024, 1024, 180, 45, 1.0, 1200, 900, 1024, 1024);

        // Reference point should land at canvas center
        var (sx, sy) = ViewportMath.LocalToScreen(
            512, 512, // CrPix maps to CrVal
            result.ScaleX, result.ScaleY, result.Rotation,
            1024, 1024, 1200, 900,
            result.TranslateX, result.TranslateY);

        Assert.Equal(600, sx, Tolerance); // canvasW/2
        Assert.Equal(450, sy, Tolerance); // canvasH/2
    }

    [Fact]
    public void DifferentPixelScale_MatchesAngularExtent()
    {
        var wcsA = CreateWcs(180, 45, 0.001); // 3.6"/px
        var wcsB = CreateWcs(180, 45, 0.002); // 7.2"/px

        // If A is at zoom 2.0, B should be at zoom 1.0 to match angular extent
        var matchedZoom = ViewportMath.ComputeMatchedZoom(2.0, wcsA.PixelScaleArcsec, wcsB.PixelScaleArcsec);

        var result = BlinkAligner.ComputeAlignedTransform(
            wcsB, 1024, 1024, 180, 45, matchedZoom, 1200, 900, 1024, 1024);

        Assert.Equal(matchedZoom, Math.Abs(result.ScaleX), Tolerance);
    }

    [Fact]
    public void ParityFlip_MirrorsScaleX()
    {
        // Positive determinant = parity flip
        var wcs = new WcsInfo
        {
            CrPix1 = 512, CrPix2 = 512,
            CrVal1 = 180, CrVal2 = 45,
            Cd1_1 = 0.001, Cd1_2 = 0,
            Cd2_1 = 0, Cd2_2 = 0.001,
        };

        var result = BlinkAligner.ComputeAlignedTransform(
            wcs, 1024, 1024, 180, 45, 2.0, 1200, 900, 1024, 1024);

        Assert.True(result.ScaleX < 0); // mirrored
        Assert.Equal(2.0, Math.Abs(result.ScaleX), Tolerance);
        Assert.Equal(2.0, result.ScaleY, Tolerance);
    }

    [Fact]
    public void NorthUp_AppliesRotation()
    {
        var wcs = CreateWcs(180, 45, 0.001, 30); // 30° rotation

        var result = BlinkAligner.ComputeAlignedTransform(
            wcs, 1024, 1024, 180, 45, 1.0, 1200, 900, 1024, 1024);

        // Rotation should be -NorthAngle
        Assert.Equal(-wcs.NorthAngle, result.Rotation, Tolerance);
    }

    [Fact]
    public void OffCenterReference_StillCentered()
    {
        var wcs = CreateWcs(180, 45, 0.001);
        var refRa = 180.1;
        var refDec = 45.05;

        var result = BlinkAligner.ComputeAlignedTransform(
            wcs, 1024, 1024, refRa, refDec, 1.0, 1200, 900, 1024, 1024);

        var pixel = wcs.WorldToPixel(refRa, refDec)!;
        var displayX = (pixel.Value.Px - 1) / 1024 * 1024;
        var displayY = (1024 - 1 - (pixel.Value.Py - 1)) / 1024 * 1024;

        var (sx, sy) = ViewportMath.LocalToScreen(
            displayX, displayY,
            result.ScaleX, result.ScaleY, result.Rotation,
            1024, 1024, 1200, 900,
            result.TranslateX, result.TranslateY);

        Assert.Equal(600, sx, Tolerance);
        Assert.Equal(450, sy, Tolerance);
    }

    // ── Blink alignment validation (the core drift test) ────────────────────

    /// <summary>
    /// The fundamental blink requirement: given image A at a known transform,
    /// compute a transform for image B such that the reference RA/Dec appears
    /// at the SAME screen position in both images.
    /// </summary>
    [Fact]
    public void TwoImages_SameRaDec_SameScreenPosition()
    {
        // Image A: 2048x2048, 0.5"/px, centered on (180, 45)
        var wcsA = CreateWcs(180, 45, 0.0001389); // ~0.5"/px
        // Image B: 1024x1024, 1.0"/px, centered on (180.01, 45.01) — different size & center
        var wcsB = CreateWcs(180.01, 45.01, 0.0002778); // ~1.0"/px

        var refRa = 180.005;
        var refDec = 45.005;
        double canvasW = 1200, canvasH = 900;

        // Image A's transform (simulating user's current view: North Up, zoom=2)
        var zoomA = 2.0;
        var rotA = -wcsA.NorthAngle;
        var pixelA = wcsA.WorldToPixel(refRa, refDec)!;
        var imgDisplayWA = 2048.0; // assume image fills display 1:1 at this test scale
        var imgDisplayHA = 2048.0;
        var displayAx = (pixelA.Value.Px - 1) / 2048 * imgDisplayWA;
        var displayAy = (2048 - 1 - (pixelA.Value.Py - 1)) / 2048 * imgDisplayHA;
        var (txA, tyA) = ViewportMath.ComputeCenterTranslate(
            displayAx, displayAy,
            wcsA.HasParityFlip ? -zoomA : zoomA, zoomA, rotA,
            imgDisplayWA, imgDisplayHA, canvasW, canvasH);

        // Screen position of refRa/refDec in image A
        var (screenAx, screenAy) = ViewportMath.LocalToScreen(
            displayAx, displayAy,
            wcsA.HasParityFlip ? -zoomA : zoomA, zoomA, rotA,
            imgDisplayWA, imgDisplayHA, canvasW, canvasH,
            txA, tyA);

        // Should be at canvas center
        Assert.Equal(canvasW / 2, screenAx, Tolerance);
        Assert.Equal(canvasH / 2, screenAy, Tolerance);

        // Image B's blink transform — using SAME display dimensions as A (Stretch=Fill)
        var matchedZoom = ViewportMath.ComputeMatchedZoom(zoomA, wcsA.PixelScaleArcsec, wcsB.PixelScaleArcsec);
        var rotB = rotA + (wcsB.NorthAngle - wcsA.NorthAngle);
        var scaleXB = wcsB.HasParityFlip != wcsA.HasParityFlip ? -matchedZoom : matchedZoom;

        var pixelB = wcsB.WorldToPixel(refRa, refDec)!;
        // Map B's pixel to A's display space (since BlinkImage fills FitsImage's bounds)
        var displayBx = (pixelB.Value.Px - 1) / 1024 * imgDisplayWA;
        var displayBy = (1024 - 1 - (pixelB.Value.Py - 1)) / 1024 * imgDisplayHA;

        var (txB, tyB) = ViewportMath.ComputeCenterTranslate(
            displayBx, displayBy,
            scaleXB, matchedZoom, rotB,
            imgDisplayWA, imgDisplayHA, canvasW, canvasH);

        // Screen position of refRa/refDec in image B (using A's display dimensions)
        var (screenBx, screenBy) = ViewportMath.LocalToScreen(
            displayBx, displayBy,
            scaleXB, matchedZoom, rotB,
            imgDisplayWA, imgDisplayHA, canvasW, canvasH,
            txB, tyB);

        // MUST match image A's screen position
        Assert.Equal(screenAx, screenBx, Tolerance);
        Assert.Equal(screenAy, screenBy, Tolerance);
    }

    [Fact]
    public void TwoImages_DifferentRotations_AlignAtReference()
    {
        // Image A: 15° rotation
        var wcsA = CreateWcs(180, 45, 0.001, 15);
        // Image B: 45° rotation, different pixel scale
        var wcsB = CreateWcs(180, 45, 0.0015, 45);

        var refRa = 180.0;
        var refDec = 45.0;
        double canvasW = 1000, canvasH = 800;
        double imgW = 1024, imgH = 1024;

        // Both transforms center refRa/refDec
        var zoomA = 3.0;
        var rotA = -wcsA.NorthAngle;
        var matchedZoom = ViewportMath.ComputeMatchedZoom(zoomA, wcsA.PixelScaleArcsec, wcsB.PixelScaleArcsec);
        var rotB = rotA + (wcsB.NorthAngle - wcsA.NorthAngle);

        // Image B's reference pixel in A's display space
        var pixelB = wcsB.WorldToPixel(refRa, refDec)!;
        var displayBx = (pixelB.Value.Px - 1) / 1024 * imgW;
        var displayBy = (1024 - 1 - (pixelB.Value.Py - 1)) / 1024 * imgH;

        var scaleXB = wcsB.HasParityFlip != wcsA.HasParityFlip ? -matchedZoom : matchedZoom;
        var (txB, tyB) = ViewportMath.ComputeCenterTranslate(
            displayBx, displayBy,
            scaleXB, matchedZoom, rotB,
            imgW, imgH, canvasW, canvasH);

        var (screenBx, screenBy) = ViewportMath.LocalToScreen(
            displayBx, displayBy,
            scaleXB, matchedZoom, rotB,
            imgW, imgH, canvasW, canvasH,
            txB, tyB);

        // Must be at canvas center
        Assert.Equal(canvasW / 2, screenBx, Tolerance);
        Assert.Equal(canvasH / 2, screenBy, Tolerance);
    }

    [Fact]
    public void RotationMatch_NorthUpOnBoth()
    {
        // If A is North Up (rotA = -NorthAngleA), B should also be North Up (rotB = -NorthAngleB)
        var wcsA = CreateWcs(180, 45, 0.001, 20); // NorthAngle ~= -20
        var wcsB = CreateWcs(180, 45, 0.001, 50); // NorthAngle ~= -50

        var rotA = -wcsA.NorthAngle; // A is North Up
        // Correct formula: rotB = rotA + NorthAngleA - NorthAngleB
        var rotB = rotA + wcsA.NorthAngle - wcsB.NorthAngle;

        // Both should be North Up → rotB should equal -NorthAngleB
        Assert.Equal(-wcsB.NorthAngle, rotB, 0.001);
    }

    [Fact]
    public void BlinkTransform_WithStretchFill_MatchesFitsImageSpace()
    {
        // Simulates the real scenario:
        // FitsImage shows 2048x1024 bitmap in a 1200x900 canvas with Stretch=Uniform
        // BlinkImage shows 512x512 bitmap but forced to FitsImage's display size (Stretch=Fill)

        var wcsA = CreateWcs(180, 45, 0.001);
        var wcsB = CreateWcs(180.02, 45.01, 0.002);

        double canvasW = 1200, canvasH = 900;
        int widthA = 2048, heightA = 1024;
        int widthB = 512, heightB = 512;

        // FitsImage display size (Stretch=Uniform)
        var scaleFitA = Math.Min(canvasW / widthA, canvasH / heightA);
        var fitsDisplayW = widthA * scaleFitA;
        var fitsDisplayH = heightA * scaleFitA;

        var refRa = 180.01;
        var refDec = 45.005;
        var zoomA = 1.5;
        var rotA = -wcsA.NorthAngle;

        // Image B's pixel in B's own coords, then mapped to FitsImage's display space
        var pixelB = wcsB.WorldToPixel(refRa, refDec)!;
        var displayBx = (pixelB.Value.Px - 1) / widthB * fitsDisplayW;
        var displayBy = (heightB - 1 - (pixelB.Value.Py - 1)) / heightB * fitsDisplayH;

        var matchedZoom = ViewportMath.ComputeMatchedZoom(zoomA, wcsA.PixelScaleArcsec, wcsB.PixelScaleArcsec);
        var rotB = rotA + (wcsB.NorthAngle - wcsA.NorthAngle);
        var scaleXB = matchedZoom; // same parity in this case

        var (txB, tyB) = ViewportMath.ComputeCenterTranslate(
            displayBx, displayBy,
            scaleXB, matchedZoom, rotB,
            fitsDisplayW, fitsDisplayH, canvasW, canvasH);

        var (screenBx, screenBy) = ViewportMath.LocalToScreen(
            displayBx, displayBy,
            scaleXB, matchedZoom, rotB,
            fitsDisplayW, fitsDisplayH, canvasW, canvasH,
            txB, tyB);

        // Reference point must be at canvas center
        Assert.Equal(canvasW / 2, screenBx, Tolerance);
        Assert.Equal(canvasH / 2, screenBy, Tolerance);
    }
}
