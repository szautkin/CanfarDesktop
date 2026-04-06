using CanfarDesktop.Models.Fits;

namespace CanfarDesktop.Helpers;

/// <summary>
/// Pure math for aligning two FITS images for blink comparison.
/// Computes CompositeTransform parameters so both images show the same sky region.
/// </summary>
public static class BlinkAligner
{
    public record BlinkTransform(
        double Rotation, double ScaleX, double ScaleY,
        double TranslateX, double TranslateY);

    /// <summary>
    /// Compute transform params to display an image aligned to a reference view.
    /// The image is rotated North-up, zoomed to match the target angular extent,
    /// and translated so the reference RA/Dec is at canvas center.
    /// </summary>
    public static BlinkTransform ComputeAlignedTransform(
        WcsInfo wcs, int imageWidth, int imageHeight,
        double referenceRa, double referenceDec,
        double targetZoomMag,
        double canvasW, double canvasH,
        double imgDisplayW, double imgDisplayH)
    {
        // 1. Rotation: North up
        var rotation = -wcs.NorthAngle;

        // 2. Scale: target zoom magnitude, with parity flip if needed
        var scaleX = wcs.HasParityFlip ? -targetZoomMag : targetZoomMag;
        var scaleY = targetZoomMag;

        // 3. Translate: center reference RA/Dec on canvas
        var pixel = wcs.WorldToPixel(referenceRa, referenceDec);
        if (pixel is null)
            return new BlinkTransform(rotation, scaleX, scaleY, 0, 0);

        // Convert 1-based FITS pixel → 0-based display pixel → Image-local coordinate
        var displayX = (pixel.Value.Px - 1) / imageWidth * imgDisplayW;
        var displayY = (imageHeight - 1 - (pixel.Value.Py - 1)) / imageHeight * imgDisplayH;

        var (tx, ty) = ViewportMath.ComputeCenterTranslate(
            displayX, displayY,
            scaleX, scaleY, rotation,
            imgDisplayW, imgDisplayH,
            canvasW, canvasH);

        return new BlinkTransform(rotation, scaleX, scaleY, tx, ty);
    }
}
