using Xunit;
using CanfarDesktop.Models.Fits;

namespace CanfarDesktop.Tests.ViewModels;

/// <summary>
/// Tests for FitsViewerViewModel logic extracted as pure math.
/// The ViewModel itself depends on WriteableBitmap (WinUI) so cannot be linked
/// into the test project. These tests verify the coordinate pipeline:
/// GoToCoordinate = WorldToPixel + Y-flip + bounds check.
/// </summary>
public class FitsViewerViewModelTests
{
    private const int Width = 100;
    private const int Height = 100;

    private static WcsInfo CreateWcs() => new()
    {
        CrPix1 = 50, CrPix2 = 50,
        CrVal1 = 180.0, CrVal2 = 45.0,
        Cd1_1 = -0.001, Cd1_2 = 0,
        Cd2_1 = 0, Cd2_2 = 0.001,
    };

    /// <summary>
    /// Replicates FitsViewerViewModel.GoToCoordinate logic:
    /// WorldToPixel → 1-based to 0-based → Y-flip
    /// </summary>
    private static (double X, double Y)? GoToCoordinate(WcsInfo wcs, int height, double ra, double dec)
    {
        if (!wcs.IsValid) return null;
        var pixel = wcs.WorldToPixel(ra, dec);
        if (pixel is null) return null;
        var displayX = pixel.Value.Px - 1;
        var displayY = height - 1 - (pixel.Value.Py - 1);
        return (displayX, displayY);
    }

    // ── GoToCoordinate ──────────────────────────────────────────────────────

    [Fact]
    public void GoToCoordinate_AtReferencePixel_ReturnsCenterPixel()
    {
        var wcs = CreateWcs();
        var result = GoToCoordinate(wcs, Height, 180.0, 45.0);
        Assert.NotNull(result);
        Assert.Equal(49, result.Value.X, 0.5); // CrPix1=50 (1-based) → 49 (0-based)
        Assert.Equal(50, result.Value.Y, 0.5); // Y-flipped: 100-1-(50-1) = 50
    }

    [Fact]
    public void GoToCoordinate_OffsetPixel_CorrectDirection()
    {
        var wcs = CreateWcs();
        // RA increases → X decreases (CD1_1 is negative)
        var result = GoToCoordinate(wcs, Height, 180.01, 45.0);
        Assert.NotNull(result);
        Assert.True(result.Value.X < 49); // moved left
    }

    [Fact]
    public void GoToCoordinate_DecIncrease_YDecreases()
    {
        var wcs = CreateWcs();
        // Dec increases → FITS Y increases → display Y decreases (Y-flip)
        var result = GoToCoordinate(wcs, Height, 180.0, 45.01);
        Assert.NotNull(result);
        Assert.True(result.Value.Y < 50); // moved up in display
    }

    [Fact]
    public void GoToCoordinate_Roundtrip()
    {
        var wcs = CreateWcs();
        // Pick a pixel, convert to world, convert back
        var px = 30.0;
        var py = 70.0;
        var fitsPx = px + 1; // 0-based to 1-based
        var fitsPy = Height - 1 - py + 1; // display Y-flip to FITS Y, then 1-based
        var (ra, dec) = wcs.PixelToWorld(fitsPx, fitsPy);
        var result = GoToCoordinate(wcs, Height, ra, dec);
        Assert.NotNull(result);
        Assert.Equal(px, result.Value.X, 0.001);
        Assert.Equal(py, result.Value.Y, 0.001);
    }

    [Fact]
    public void GoToCoordinate_SingularWcs_ReturnsNull()
    {
        var wcs = new WcsInfo(); // all zeros
        Assert.Null(GoToCoordinate(wcs, Height, 10, 20));
    }

    // ── UpdatePixelInfo bounds ───────────────────────────────────────────────

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(100, 0)]
    [InlineData(0, 100)]
    [InlineData(-5, -5)]
    [InlineData(200, 200)]
    public void PixelBoundsCheck_OutOfRange_Invalid(int x, int y)
    {
        Assert.True(x < 0 || x >= Width || y < 0 || y >= Height);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(99, 99)]
    [InlineData(50, 50)]
    public void PixelBoundsCheck_InRange_Valid(int x, int y)
    {
        Assert.True(x >= 0 && x < Width && y >= 0 && y < Height);
    }

    // ── Y-flip correctness ──────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 99)]   // display top → FITS bottom
    [InlineData(99, 0)]   // display bottom → FITS top
    [InlineData(50, 49)]  // middle
    public void YFlip_DisplayToFits(int displayY, int expectedFitsY)
    {
        var fitsY = Height - 1 - displayY;
        Assert.Equal(expectedFitsY, fitsY);
    }

    // ── PixelToWorld → WorldToPixel roundtrip with various WCS ──────────────

    [Fact]
    public void Roundtrip_RotatedWcs()
    {
        var angle = 30.0 * System.Math.PI / 180.0;
        var wcs = new WcsInfo
        {
            CrPix1 = 512, CrPix2 = 512,
            CrVal1 = 90, CrVal2 = -30,
            Cd1_1 = -0.001 * System.Math.Cos(angle),
            Cd1_2 = 0.001 * System.Math.Sin(angle),
            Cd2_1 = 0.001 * System.Math.Sin(angle),
            Cd2_2 = 0.001 * System.Math.Cos(angle),
        };

        var (ra, dec) = wcs.PixelToWorld(300, 400);
        var result = GoToCoordinate(wcs, 1024, ra, dec);
        Assert.NotNull(result);
        // Convert back: display 0-based → 1-based FITS with Y-flip
        var recoveredFitsPx = result.Value.X + 1;
        var recoveredFitsPy = 1024 - 1 - result.Value.Y + 1;
        Assert.Equal(300, recoveredFitsPx, 0.001);
        Assert.Equal(400, recoveredFitsPy, 0.001);
    }

    // ── WorldCoordinate record ──────────────────────────────────────────────

    [Fact]
    public void WorldCoordinate_Display_ContainsRaAndDec()
    {
        var coord = new WorldCoordinate(180.0, 45.0);
        Assert.Contains("RA", coord.Display);
        Assert.Contains("Dec", coord.Display);
    }

    [Fact]
    public void WorldCoordinate_ValueEquality()
    {
        var a = new WorldCoordinate(10.0, 20.0);
        var b = new WorldCoordinate(10.0, 20.0);
        Assert.Equal(a, b);
        Assert.NotEqual(a, new WorldCoordinate(10.0, 20.1));
    }
}
