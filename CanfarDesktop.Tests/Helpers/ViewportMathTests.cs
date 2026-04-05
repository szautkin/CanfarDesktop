using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

public class ViewportMathTests
{
    private const double Tolerance = 0.0001;

    // Common test setup: 1000x800 image centered in a 1200x900 canvas
    private const double ImgW = 1000, ImgH = 800;
    private const double CanvasW = 1200, CanvasH = 900;

    [Fact]
    public void Roundtrip_NoTransform()
    {
        // Identity: scale=1, rotation=0, translate=0
        var (sx, sy) = ViewportMath.LocalToScreen(500, 400, 1, 1, 0, ImgW, ImgH, CanvasW, CanvasH, 0, 0);
        var (lx, ly) = ViewportMath.ScreenToLocal(sx, sy, 1, 1, 0, ImgW, ImgH, CanvasW, CanvasH, 0, 0);
        Assert.Equal(500, lx, Tolerance);
        Assert.Equal(400, ly, Tolerance);
    }

    [Fact]
    public void Roundtrip_WithScale()
    {
        var (sx, sy) = ViewportMath.LocalToScreen(300, 200, 2, 2, 0, ImgW, ImgH, CanvasW, CanvasH, 0, 0);
        var (lx, ly) = ViewportMath.ScreenToLocal(sx, sy, 2, 2, 0, ImgW, ImgH, CanvasW, CanvasH, 0, 0);
        Assert.Equal(300, lx, Tolerance);
        Assert.Equal(200, ly, Tolerance);
    }

    [Fact]
    public void Roundtrip_WithRotation()
    {
        var (sx, sy) = ViewportMath.LocalToScreen(300, 200, 1, 1, 45, ImgW, ImgH, CanvasW, CanvasH, 0, 0);
        var (lx, ly) = ViewportMath.ScreenToLocal(sx, sy, 1, 1, 45, ImgW, ImgH, CanvasW, CanvasH, 0, 0);
        Assert.Equal(300, lx, Tolerance);
        Assert.Equal(200, ly, Tolerance);
    }

    [Fact]
    public void Roundtrip_WithTranslate()
    {
        var (sx, sy) = ViewportMath.LocalToScreen(300, 200, 1, 1, 0, ImgW, ImgH, CanvasW, CanvasH, 50, -30);
        var (lx, ly) = ViewportMath.ScreenToLocal(sx, sy, 1, 1, 0, ImgW, ImgH, CanvasW, CanvasH, 50, -30);
        Assert.Equal(300, lx, Tolerance);
        Assert.Equal(200, ly, Tolerance);
    }

    [Fact]
    public void Roundtrip_ScaleRotateTranslate()
    {
        var (sx, sy) = ViewportMath.LocalToScreen(750, 100, 3, 3, -30, ImgW, ImgH, CanvasW, CanvasH, 100, -50);
        var (lx, ly) = ViewportMath.ScreenToLocal(sx, sy, 3, 3, -30, ImgW, ImgH, CanvasW, CanvasH, 100, -50);
        Assert.Equal(750, lx, Tolerance);
        Assert.Equal(100, ly, Tolerance);
    }

    [Fact]
    public void Roundtrip_WithMirror()
    {
        // Negative scaleX = horizontal mirror (parity flip)
        var (sx, sy) = ViewportMath.LocalToScreen(300, 200, -2, 2, 15, ImgW, ImgH, CanvasW, CanvasH, 20, 10);
        var (lx, ly) = ViewportMath.ScreenToLocal(sx, sy, -2, 2, 15, ImgW, ImgH, CanvasW, CanvasH, 20, 10);
        Assert.Equal(300, lx, Tolerance);
        Assert.Equal(200, ly, Tolerance);
    }

    [Fact]
    public void Roundtrip_90DegreeRotation()
    {
        var (sx, sy) = ViewportMath.LocalToScreen(100, 700, 1, 1, 90, ImgW, ImgH, CanvasW, CanvasH, 0, 0);
        var (lx, ly) = ViewportMath.ScreenToLocal(sx, sy, 1, 1, 90, ImgW, ImgH, CanvasW, CanvasH, 0, 0);
        Assert.Equal(100, lx, Tolerance);
        Assert.Equal(700, ly, Tolerance);
    }

    [Fact]
    public void Roundtrip_180DegreeRotation()
    {
        var (sx, sy) = ViewportMath.LocalToScreen(100, 700, 1.5, 1.5, 180, ImgW, ImgH, CanvasW, CanvasH, 0, 0);
        var (lx, ly) = ViewportMath.ScreenToLocal(sx, sy, 1.5, 1.5, 180, ImgW, ImgH, CanvasW, CanvasH, 0, 0);
        Assert.Equal(100, lx, Tolerance);
        Assert.Equal(700, ly, Tolerance);
    }

    [Fact]
    public void ImageCenter_MapsToCanvasCenter_NoTransform()
    {
        // Image center (500, 400) with no transform should map to canvas center
        var (sx, sy) = ViewportMath.LocalToScreen(ImgW / 2, ImgH / 2, 1, 1, 0, ImgW, ImgH, CanvasW, CanvasH, 0, 0);
        Assert.Equal(CanvasW / 2, sx, Tolerance);
        Assert.Equal(CanvasH / 2, sy, Tolerance);
    }

    [Fact]
    public void ImageCenter_StaysAtCanvasCenter_WithRotation()
    {
        // Center point should stay fixed under any rotation (since we rotate around center)
        var (sx, sy) = ViewportMath.LocalToScreen(ImgW / 2, ImgH / 2, 1, 1, 73, ImgW, ImgH, CanvasW, CanvasH, 0, 0);
        Assert.Equal(CanvasW / 2, sx, Tolerance);
        Assert.Equal(CanvasH / 2, sy, Tolerance);
    }

    [Fact]
    public void ComputeCenterTranslate_CentersPoint()
    {
        // After applying the translate, the target point should be at canvas center
        var localX = 200.0;
        var localY = 600.0;
        var (tx, ty) = ViewportMath.ComputeCenterTranslate(localX, localY, 2, 2, 30, ImgW, ImgH, CanvasW, CanvasH);
        var (sx, sy) = ViewportMath.LocalToScreen(localX, localY, 2, 2, 30, ImgW, ImgH, CanvasW, CanvasH, tx, ty);
        Assert.Equal(CanvasW / 2, sx, Tolerance);
        Assert.Equal(CanvasH / 2, sy, Tolerance);
    }

    [Fact]
    public void ComputeCenterTranslate_WithMirror()
    {
        var localX = 800.0;
        var localY = 100.0;
        var (tx, ty) = ViewportMath.ComputeCenterTranslate(localX, localY, -3, 3, -15, ImgW, ImgH, CanvasW, CanvasH);
        var (sx, sy) = ViewportMath.LocalToScreen(localX, localY, -3, 3, -15, ImgW, ImgH, CanvasW, CanvasH, tx, ty);
        Assert.Equal(CanvasW / 2, sx, Tolerance);
        Assert.Equal(CanvasH / 2, sy, Tolerance);
    }

    [Fact]
    public void ComputeZoomTranslate_KeepsCursorPointFixed()
    {
        double cursorX = 700, cursorY = 300;
        double oldScale = 2, newScale = 4;

        // Find what's under cursor at old scale
        var (localX, localY) = ViewportMath.ScreenToLocal(cursorX, cursorY, oldScale, oldScale, 0,
            ImgW, ImgH, CanvasW, CanvasH, 0, 0);

        // Compute new translate
        var (tx, ty) = ViewportMath.ComputeZoomTranslate(cursorX, cursorY,
            oldScale, oldScale, newScale, newScale, 0,
            ImgW, ImgH, CanvasW, CanvasH, 0, 0);

        // Verify same local point maps back to cursor with new scale
        var (sx, sy) = ViewportMath.LocalToScreen(localX, localY, newScale, newScale, 0,
            ImgW, ImgH, CanvasW, CanvasH, tx, ty);
        Assert.Equal(cursorX, sx, Tolerance);
        Assert.Equal(cursorY, sy, Tolerance);
    }

    [Fact]
    public void ComputeZoomTranslate_WithRotation()
    {
        double cursorX = 400, cursorY = 500;
        double oldScale = 1.5, newScale = 5;

        var (localX, localY) = ViewportMath.ScreenToLocal(cursorX, cursorY, oldScale, oldScale, 45,
            ImgW, ImgH, CanvasW, CanvasH, 30, -20);

        var (tx, ty) = ViewportMath.ComputeZoomTranslate(cursorX, cursorY,
            oldScale, oldScale, newScale, newScale, 45,
            ImgW, ImgH, CanvasW, CanvasH, 30, -20);

        var (sx, sy) = ViewportMath.LocalToScreen(localX, localY, newScale, newScale, 45,
            ImgW, ImgH, CanvasW, CanvasH, tx, ty);
        Assert.Equal(cursorX, sx, Tolerance);
        Assert.Equal(cursorY, sy, Tolerance);
    }

    [Fact]
    public void ComputeZoomTranslate_WithMirrorAndRotation()
    {
        double cursorX = 600, cursorY = 200;
        double oldScaleX = -2, newScaleX = -5;

        var (localX, localY) = ViewportMath.ScreenToLocal(cursorX, cursorY, oldScaleX, 2, 30,
            ImgW, ImgH, CanvasW, CanvasH, 10, 10);

        var (tx, ty) = ViewportMath.ComputeZoomTranslate(cursorX, cursorY,
            oldScaleX, 2, newScaleX, 5, 30,
            ImgW, ImgH, CanvasW, CanvasH, 10, 10);

        var (sx, sy) = ViewportMath.LocalToScreen(localX, localY, newScaleX, 5, 30,
            ImgW, ImgH, CanvasW, CanvasH, tx, ty);
        Assert.Equal(cursorX, sx, Tolerance);
        Assert.Equal(cursorY, sy, Tolerance);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    [InlineData(-45)]
    public void Roundtrip_VariousRotations(double rotation)
    {
        var (sx, sy) = ViewportMath.LocalToScreen(250, 600, 2.5, 2.5, rotation, ImgW, ImgH, CanvasW, CanvasH, 30, -15);
        var (lx, ly) = ViewportMath.ScreenToLocal(sx, sy, 2.5, 2.5, rotation, ImgW, ImgH, CanvasW, CanvasH, 30, -15);
        Assert.Equal(250, lx, Tolerance);
        Assert.Equal(600, ly, Tolerance);
    }
}
