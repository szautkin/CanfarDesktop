using Xunit;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Services.CubeViewer;
using CanfarDesktop.Views.CubeViewer;

namespace CanfarDesktop.Tests.Views;

/// <summary>
/// Where a mark lands in the cube — in the volume, and on the slice. Both are arithmetic, and both are
/// testable without a GPU because the projection was pulled out of the renderer to be shared.
/// </summary>
public class CubeAnnotationSurfaceTests
{
    private const int Nx = 100, Ny = 80, Nz = 40;

    /// <summary>Looking straight down the spectral axis at a cube of the usual proportions.</summary>
    private static CubeProjector Projector(float azimuth = 0, float elevation = 0)
        => CubeProjector.Create(azimuth, elevation, distance: 3f, spectralScale: 1f,
            volNx: Nx, volNy: Ny, widthDip: 800, heightDip: 600)!;

    private static CubeVolumeAnnotationSurface Volume(float azimuth = 0, float elevation = 0)
        => new(Projector(azimuth, elevation), Nx, Ny, Nz);

    // ── The volume view ─────────────────────────────────────────────────────────────────────────

    /// <summary>The centre voxel projects to the centre of the panel, whatever the camera is doing.</summary>
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.7f, 0.4f)]
    [InlineData(-1.2f, -0.3f)]
    public void TheMiddleOfTheCubeIsTheMiddleOfThePanel(float azimuth, float elevation)
    {
        var centre = AnnotationAnchor.Data((Nx - 1) / 2.0, (Ny - 1) / 2.0, (Nz - 1) / 2.0);
        var at = Volume(azimuth, elevation).Project(centre);

        Assert.NotNull(at);
        Assert.Equal(400, at!.Value.X, 1);
        Assert.Equal(300, at.Value.Y, 1);
    }

    /// <summary>
    /// Screen y runs down and the box's y runs up, so a voxel at a HIGHER y is HIGHER on the panel.
    /// Getting this backwards puts every mark on the wrong side of the cube, which is the kind of thing
    /// that looks plausible in a screenshot.
    /// </summary>
    [Fact]
    public void TheBoxIsTheRightWayUp()
    {
        var surface = Volume();
        var low = surface.Project(AnnotationAnchor.Data(50, 0, 20))!.Value;
        var high = surface.Project(AnnotationAnchor.Data(50, Ny - 1, 20))!.Value;

        Assert.True(high.Y < low.Y);
    }

    /// <summary>Only cube marks belong in a cube; another viewer's anchor is skipped, not clamped.</summary>
    [Theory]
    [InlineData(AnchorSpace.ImagePixel)]
    [InlineData(AnchorSpace.Sky)]
    public void AnotherViewersMarkIsNotPlacedInTheVolume(AnchorSpace space)
    {
        var anchor = space == AnchorSpace.Sky
            ? AnnotationAnchor.Sky(10, 41)
            : AnnotationAnchor.ImagePixel(10, 20);

        Assert.Null(Volume().Project(anchor));
    }

    [Fact]
    public void WithNoPanelToProjectOntoNothingIsPlaced()
    {
        Assert.Null(CubeProjector.Create(0, 0, 3f, 1f, Nx, Ny, widthDip: 0, heightDip: 0));

        var surface = new CubeVolumeAnnotationSurface(null, Nx, Ny, Nz);
        Assert.Null(surface.Project(AnnotationAnchor.Data(1, 2, 3)));
        Assert.Equal(1.0, surface.UnitsToPixels(AnnotationAnchor.Data(1, 2, 3)));
    }

    /// <summary>
    /// The view is perspective, so a voxel near the camera covers more screen than one at the back. A
    /// single scale for the whole cube would draw the far marks too big and the near ones too small.
    /// </summary>
    [Fact]
    public void AVoxelCoversMoreScreenNearTheCameraThanFarFromIt()
    {
        // Looking along +Z from in front: channel 0 is at the BACK of the box, the last channel nearest.
        var surface = Volume();
        var near = surface.UnitsToPixels(AnnotationAnchor.Data(50, 40, Nz - 1));
        var far = surface.UnitsToPixels(AnnotationAnchor.Data(50, 40, 0));

        Assert.True(near > far, $"near {near} should cover more than far {far}");
    }

    // ── The slice view ──────────────────────────────────────────────────────────────────────────

    private static CubeSliceAnnotationSurface Slice(
        int channel = 10, double zoom = 1, double panX = 0, double panY = 0,
        int displayNx = Nx, int displayNy = Ny)
        => new(channel, Nx, Ny, displayNx, displayNy,
               viewportWidth: 400, viewportHeight: 400, zoom, panX, panY);

    /// <summary>
    /// A mark belongs to its channel. Drawing every mark on every slice would make a cube with marks on
    /// forty channels unreadable — and would say something false, because the mark is about that channel.
    /// </summary>
    [Fact]
    public void AMarkIsOnlyOnTheSliceShowingItsChannel()
    {
        var mark = AnnotationAnchor.Data(50, 40, 10);

        Assert.NotNull(Slice(channel: 10).Project(mark));
        Assert.Null(Slice(channel: 11).Project(mark));
        Assert.Null(Slice(channel: 0).Project(mark));
    }

    /// <summary>A mark placed at 16.5 belongs to 17 as much as to 16, and the scrubber only sits on whole channels.</summary>
    [Fact]
    public void AChannelBetweenTwoRoundsToTheNearer()
    {
        Assert.NotNull(Slice(channel: 17).Project(AnnotationAnchor.Data(50, 40, 16.5)));
        Assert.NotNull(Slice(channel: 16).Project(AnnotationAnchor.Data(50, 40, 16.4)));
    }

    /// <summary>
    /// The slice is fitted into the viewport with its aspect kept, so a 100x80 slice in a 400x400 view
    /// is letterboxed: 4 pixels per slice pixel, with 40 pixels of margin above and below.
    /// </summary>
    [Fact]
    public void TheSliceIsFittedWithItsAspectKept()
    {
        var origin = Slice().Project(AnnotationAnchor.Data(0, 0, 10))!.Value;

        Assert.Equal(0, origin.X, 6);
        Assert.Equal(40, origin.Y, 6);
        Assert.Equal(4.0, Slice().UnitsToPixels(AnnotationAnchor.Data(0, 0, 10)), 6);
    }

    [Fact]
    public void ZoomAndPanMoveAMarkWithTheImage()
    {
        var at1 = Slice().Project(AnnotationAnchor.Data(50, 40, 10))!.Value;
        var zoomed = Slice(zoom: 2).Project(AnnotationAnchor.Data(50, 40, 10))!.Value;
        var panned = Slice(panX: 25, panY: -10).Project(AnnotationAnchor.Data(50, 40, 10))!.Value;

        // The centre voxel is at the viewport centre, so zooming about the centre leaves it there.
        Assert.Equal(at1.X, zoomed.X, 6);
        Assert.Equal(at1.Y, zoomed.Y, 6);

        Assert.Equal(at1.X + 25, panned.X, 6);
        Assert.Equal(at1.Y - 10, panned.Y, 6);
        Assert.Equal(8.0, Slice(zoom: 2).UnitsToPixels(AnnotationAnchor.Data(0, 0, 10)), 6);
    }

    /// <summary>
    /// The slice is rendered from a DOWN-SAMPLED volume, so a voxel and a displayed pixel are not the
    /// same thing. Anchors are in the cube's own voxels — half the display resolution here means half
    /// the pixels per voxel.
    /// </summary>
    [Fact]
    public void AVoxelIsConvertedToTheDownSampledSlicesOwnPixels()
    {
        var halved = Slice(displayNx: Nx / 2, displayNy: Ny / 2);

        // 50x40 display pixels fitted into 400x400 → 8 per display pixel, and one voxel is half of one.
        Assert.Equal(4.0, halved.UnitsToPixels(AnnotationAnchor.Data(0, 0, 10)), 6);

        var lastVoxel = halved.Project(AnnotationAnchor.Data(Nx, Ny, 10))!.Value;
        var full = Slice().Project(AnnotationAnchor.Data(Nx, Ny, 10))!.Value;
        Assert.Equal(full.X, lastVoxel.X, 6);
        Assert.Equal(full.Y, lastVoxel.Y, 6);
    }

    [Fact]
    public void WithNoViewportNothingIsPlaced()
    {
        var collapsed = new CubeSliceAnnotationSurface(10, Nx, Ny, Nx, Ny, 0, 0, 1, 0, 0);

        Assert.Null(collapsed.Project(AnnotationAnchor.Data(50, 40, 10)));
        Assert.Equal(1.0, collapsed.UnitsToPixels(AnnotationAnchor.Data(50, 40, 10)));
    }

    [Fact]
    public void AnImagePixelMarkIsNotPlacedOnASlice()
        => Assert.Null(Slice().Project(AnnotationAnchor.ImagePixel(50, 40)));
}
