using Xunit;
using CanfarDesktop.Services.CubeViewer;

namespace CanfarDesktop.Tests.Services.CubeViewer;

/// <summary>
/// The cube's camera arithmetic, both ways.
///
/// The forward direction places marks and axis captions on the volume. The inverse is what lets a press
/// on the volume BE a voxel: a press on a perspective view names a ray, and the channel on screen
/// supplies the depth. The two have to agree exactly, or a mark lands somewhere other than where it was
/// put — which reads as the mark being wrong rather than the projection.
/// </summary>
public class CubeProjectorTests
{
    private const int Nx = 180, Ny = 90, Nz = 201;

    /// <summary>
    /// How far out the round trip may land, in voxels.
    ///
    /// The camera matrices are single-precision — the GPU's are, and the overlay has to use the same
    /// ones — and a ray meets a channel plane at a shallow angle when the camera is steeply elevated,
    /// which is where that precision shows. A tenth of a voxel is well under what anybody can point at,
    /// and a wrong inverse misses by tens of voxels or returns nothing at all, so this still catches
    /// every failure worth catching.
    /// </summary>
    private const double MaxVoxelError = 0.1;

    /// <summary>Asserts a voxel came back where it went, and says how far out it was when it did not.</summary>
    private static void Same(double expected, double actual, string axis)
        => Assert.True(Math.Abs(expected - actual) <= MaxVoxelError,
            $"{axis} came back {Math.Abs(expected - actual):0.####} voxels out: expected {expected}, got {actual}");

    private static CubeProjector Projector(
        float azimuth = 0.6f, float elevation = 0.3f, float distance = 3f, float spectralScale = 1f)
        => CubeProjector.Create(azimuth, elevation, distance, spectralScale, Nx, Ny, 1600, 900)!;

    [Fact]
    public void APanelWithNoAreaHasNoProjector()
        => Assert.Null(CubeProjector.Create(0.6f, 0.3f, 3f, 1f, Nx, Ny, 0, 0));

    // ── The round trip ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The property that matters: project a voxel, press exactly there, get the same voxel back.
    ///
    /// Run across several camera angles, because an inverse that only holds looking straight down an
    /// axis is one that fails the moment anybody orbits — which is the first thing they do.
    /// </summary>
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.6f, 0.3f)]
    [InlineData(-1.2f, -0.4f)]
    [InlineData(2.4f, 0.9f)]
    public void AProjectedVoxelComesBackFromWhereItLanded(float azimuth, float elevation)
    {
        var projector = Projector(azimuth, elevation);
        const int channel = 100;

        foreach (var (x, y) in new[] { (90.0, 45.0), (10.0, 10.0), (170.0, 80.0), (0.0, 0.0) })
        {
            var on = projector.ProjectVoxel(x, y, channel, Nx, Ny, Nz);
            Assert.True(on.Visible, $"voxel {x},{y} should be in front of the camera");

            var back = projector.VoxelOnChannelPlane(on.X, on.Y, channel, Nx, Ny, Nz);

            Assert.NotNull(back);
            Same(x, back!.Value.X, "x");
            Same(y, back.Value.Y, "y");
        }
    }

    /// <summary>The spectral scale stretches the box along the channel axis; the inverse must undo it.</summary>
    [Theory]
    [InlineData(0.4f)]
    [InlineData(1f)]
    [InlineData(3f)]
    public void TheRoundTripSurvivesTheSpectralScale(float spectralScale)
    {
        var projector = Projector(spectralScale: spectralScale);
        var on = projector.ProjectVoxel(120, 30, 40, Nx, Ny, Nz);

        var back = projector.VoxelOnChannelPlane(on.X, on.Y, 40, Nx, Ny, Nz);

        Assert.NotNull(back);
        Same(120, back!.Value.X, "x");
        Same(30, back.Value.Y, "y");
    }

    /// <summary>
    /// Every channel is its own plane, so one screen point means a different voxel on each — which is
    /// the whole mechanism, and the reason the channel has to be part of the question.
    ///
    /// Neighbouring channels rather than the two ends: a ray aimed through a point on one face need not
    /// cross a distant plane inside the box at all, and that refusal is correct behaviour rather than
    /// something to design a test around.
    /// </summary>
    [Fact]
    public void TheSamePressOnDifferentChannelsIsDifferentVoxels()
    {
        var projector = Projector();
        var on = projector.ProjectVoxel(90, 45, 100, Nx, Ny, Nz);

        var here = projector.VoxelOnChannelPlane(on.X, on.Y, 100, Nx, Ny, Nz);
        var next = projector.VoxelOnChannelPlane(on.X, on.Y, 110, Nx, Ny, Nz);

        Assert.NotNull(here);
        Assert.NotNull(next);
        Assert.True(Math.Abs(here!.Value.X - next!.Value.X) > MaxVoxelError
                 || Math.Abs(here.Value.Y - next.Value.Y) > MaxVoxelError,
            $"an oblique camera should meet two channel planes at different voxels, got " +
            $"({here.Value.X:0.##},{here.Value.Y:0.##}) and ({next.Value.X:0.##},{next.Value.Y:0.##})");
    }

    // ── Presses that are not voxels ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A press beside the box is not a voxel. Clamping it to the nearest face would put a mark
    /// somewhere nobody pointed at, and a mark you did not place is worse than a press that did nothing.
    /// </summary>
    [Fact]
    public void APressWellOutsideTheBoxIsRefused()
    {
        var projector = Projector();

        Assert.Null(projector.VoxelOnChannelPlane(2, 2, 100, Nx, Ny, Nz));
        Assert.Null(projector.VoxelOnChannelPlane(1598, 898, 100, Nx, Ny, Nz));
    }

    /// <summary>A press on the very corner still counts — an edge is part of the data, not beside it.</summary>
    [Fact]
    public void APressOnTheCornerVoxelIsAccepted()
    {
        var projector = Projector();
        var on = projector.ProjectVoxel(0, 0, 100, Nx, Ny, Nz);

        Assert.NotNull(projector.VoxelOnChannelPlane(on.X, on.Y, 100, Nx, Ny, Nz));
    }

    // ── Degenerate cubes ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A single-channel cube has no span to place anything along, so its one plane sits in the middle
    /// and a press on it still resolves rather than dividing by zero.
    /// </summary>
    [Fact]
    public void ASingleChannelCubeStillResolvesAPress()
    {
        var projector = CubeProjector.Create(0.6f, 0.3f, 3f, 1f, Nx, Ny, 1600, 900)!;
        var on = projector.ProjectVoxel(90, 45, 0, Nx, Ny, 1);

        var back = projector.VoxelOnChannelPlane(on.X, on.Y, 0, Nx, Ny, 1);

        Assert.NotNull(back);
        Same(90, back!.Value.X, "x");
        Same(45, back.Value.Y, "y");
    }

    [Fact]
    public void ASingleVoxelAxisCollapsesToZeroRatherThanDividingByIt()
    {
        var projector = CubeProjector.Create(0.6f, 0.3f, 3f, 1f, 1, 1, 1600, 900)!;
        var on = projector.ProjectVoxel(0, 0, 0, 1, 1, 1);

        var back = projector.VoxelOnChannelPlane(on.X, on.Y, 0, 1, 1, 1);

        Assert.NotNull(back);
        Same(0, back!.Value.X, "x");
        Same(0, back.Value.Y, "y");
    }
}
