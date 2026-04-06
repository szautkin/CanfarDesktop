namespace CanfarDesktop.Helpers;

/// <summary>
/// Pure math for viewport coordinate transforms. Matches WinUI CompositeTransform
/// with RenderTransformOrigin="0.5,0.5" (rotation/scale around image center).
///
/// Transform order: center → Scale → Rotate → Translate → uncenter
/// Forward: screen = imgOffset + center + Rotate(Scale(local - center)) + translate
/// Inverse: local = center + InvScale(InvRotate(screen - imgOffset - center - translate))
/// </summary>
public static class ViewportMath
{
    /// <summary>
    /// Convert image-local coordinates to screen coordinates.
    /// </summary>
    public static (double X, double Y) LocalToScreen(
        double localX, double localY,
        double scaleX, double scaleY, double rotationDeg,
        double imgW, double imgH,
        double canvasW, double canvasH,
        double translateX, double translateY)
    {
        var cx = imgW / 2;
        var cy = imgH / 2;
        var r = rotationDeg * Math.PI / 180;
        var cos = Math.Cos(r);
        var sin = Math.Sin(r);

        var dx = (localX - cx) * scaleX;
        var dy = (localY - cy) * scaleY;
        var rx = dx * cos - dy * sin;
        var ry = dx * sin + dy * cos;

        var imgOffsetX = (canvasW - imgW) / 2;
        var imgOffsetY = (canvasH - imgH) / 2;

        return (imgOffsetX + rx + cx + translateX,
                imgOffsetY + ry + cy + translateY);
    }

    /// <summary>
    /// Convert screen coordinates to image-local coordinates.
    /// </summary>
    public static (double X, double Y) ScreenToLocal(
        double screenX, double screenY,
        double scaleX, double scaleY, double rotationDeg,
        double imgW, double imgH,
        double canvasW, double canvasH,
        double translateX, double translateY)
    {
        var cx = imgW / 2;
        var cy = imgH / 2;
        var r = rotationDeg * Math.PI / 180;
        var cos = Math.Cos(r);
        var sin = Math.Sin(r);

        var imgOffsetX = (canvasW - imgW) / 2;
        var imgOffsetY = (canvasH - imgH) / 2;

        var dx = screenX - imgOffsetX - cx - translateX;
        var dy = screenY - imgOffsetY - cy - translateY;
        var ux = dx * cos + dy * sin;
        var uy = -dx * sin + dy * cos;

        return (ux / scaleX + cx, uy / scaleY + cy);
    }

    /// <summary>
    /// Compute the translate needed to center a given local coordinate on the canvas.
    /// </summary>
    public static (double TranslateX, double TranslateY) ComputeCenterTranslate(
        double localX, double localY,
        double scaleX, double scaleY, double rotationDeg,
        double imgW, double imgH,
        double canvasW, double canvasH)
    {
        var cx = imgW / 2;
        var cy = imgH / 2;
        var r = rotationDeg * Math.PI / 180;
        var cos = Math.Cos(r);
        var sin = Math.Sin(r);

        var dx = (localX - cx) * scaleX;
        var dy = (localY - cy) * scaleY;
        var rx = dx * cos - dy * sin;
        var ry = dx * sin + dy * cos;

        var imgOffsetX = (canvasW - imgW) / 2;
        var imgOffsetY = (canvasH - imgH) / 2;

        // We want: canvasW/2 = imgOffsetX + rx + cx + translateX
        var translateX = canvasW / 2 - imgOffsetX - rx - cx;
        var translateY = canvasH / 2 - imgOffsetY - ry - cy;
        return (translateX, translateY);
    }

    /// <summary>
    /// Compute new translate for zoom-toward-cursor (keep the point under the cursor fixed).
    /// </summary>
    public static (double TranslateX, double TranslateY) ComputeZoomTranslate(
        double cursorScreenX, double cursorScreenY,
        double oldScaleX, double oldScaleY,
        double newScaleX, double newScaleY,
        double rotationDeg,
        double imgW, double imgH,
        double canvasW, double canvasH,
        double oldTranslateX, double oldTranslateY)
    {
        // Find image-local coord under cursor with old transform
        var (localX, localY) = ScreenToLocal(
            cursorScreenX, cursorScreenY,
            oldScaleX, oldScaleY, rotationDeg,
            imgW, imgH, canvasW, canvasH,
            oldTranslateX, oldTranslateY);

        // Compute what translate keeps that local coord at the cursor with new scale
        var cx = imgW / 2;
        var cy = imgH / 2;
        var r = rotationDeg * Math.PI / 180;
        var cos = Math.Cos(r);
        var sin = Math.Sin(r);

        var dx = (localX - cx) * newScaleX;
        var dy = (localY - cy) * newScaleY;
        var rx = dx * cos - dy * sin;
        var ry = dx * sin + dy * cos;

        var imgOffsetX = (canvasW - imgW) / 2;
        var imgOffsetY = (canvasH - imgH) / 2;

        var newTx = cursorScreenX - imgOffsetX - rx - cx;
        var newTy = cursorScreenY - imgOffsetY - ry - cy;
        return (newTx, newTy);
    }

    /// <summary>
    /// Compute zoom for image B that matches the angular extent of image A.
    /// If B has coarser pixels (larger arcsec/px), it needs less zoom.
    /// </summary>
    public static double ComputeMatchedZoom(double zoomA, double pixelScaleA, double pixelScaleB)
    {
        if (pixelScaleB <= 0) return zoomA;
        return zoomA * (pixelScaleA / pixelScaleB);
    }
}
