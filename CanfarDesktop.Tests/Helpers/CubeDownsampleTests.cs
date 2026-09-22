using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// Which samples a cube is kept at.
///
/// <para>This looks like a memory-budget detail and is not. The rendered voxel grid IS the space a
/// cube mark's position is expressed in, so a stride chosen to fit a GPU texture also decides where a
/// mark can be put — and, when it collapses an axis, whether a mark can be moved at all.</para>
/// </summary>
public class CubeDownsampleTests
{
    /// <summary>
    /// The case that was reported: a JCMT HARP cube, 4x4 on the sky and 2048 deep.
    ///
    /// One stride taken from the longest of the three axes meant the spectral axis chose 8 and the sky
    /// plane was sampled with it, so CeilDiv(4, 8) gave a 1x1 plane. The sky structure was discarded
    /// outright, and because an anchor lives in this grid, a 1-wide axis left marks exactly one
    /// position they could occupy: every drag in the slice view mapped back to (0,0).
    /// </summary>
    [Fact]
    public void ADeepCubeWithATinySkyPlaneKeepsItsSkyPlane()
    {
        var plan = CubeDownsample.For(4, 4, 2048);

        Assert.Equal(4, plan.Nx);
        Assert.Equal(4, plan.Ny);
        Assert.Equal(1, plan.StrideXY);

        // The spectral axis still has to come under the cap; it is the one that is genuinely too long.
        Assert.Equal(256, plan.Nz);
        Assert.Equal(8, plan.StrideZ);
    }

    /// <summary>
    /// An axis already under the cap is never strided at all — whichever of the three it is.
    /// This is the property the collapse violated, stated in general.
    /// </summary>
    [Theory]
    [InlineData(4, 4, 2048)]
    [InlineData(2048, 4, 4)]
    [InlineData(4, 2048, 4)]
    [InlineData(1, 1, 4096)]
    [InlineData(64, 64, 64)]
    public void AnAxisThatAlreadyFitsIsKeptWhole(int nx, int ny, int nz)
    {
        var plan = CubeDownsample.For(nx, ny, nz);

        if (Math.Max(nx, ny) <= CubeDownsample.MaxDim)
        {
            Assert.Equal(nx, plan.Nx);
            Assert.Equal(ny, plan.Ny);
        }

        if (nz <= CubeDownsample.MaxDim) Assert.Equal(nz, plan.Nz);
    }

    /// <summary>The cap is the whole point of striding, so nothing may come back over it.</summary>
    [Theory]
    [InlineData(4, 4, 2048)]
    [InlineData(2048, 2048, 2048)]
    [InlineData(8000, 120, 3)]
    [InlineData(1, 1, 1000000)]
    public void NothingComesBackLargerThanTheCap(int nx, int ny, int nz)
    {
        var plan = CubeDownsample.For(nx, ny, nz);

        Assert.True(plan.Nx <= CubeDownsample.MaxDim);
        Assert.True(plan.Ny <= CubeDownsample.MaxDim);
        Assert.True(plan.Nz <= CubeDownsample.MaxDim);
    }

    /// <summary>
    /// And nothing comes back empty. A zero-length axis is not a cube, and a caller that indexed one
    /// would fault rather than misdraw.
    /// </summary>
    [Theory]
    [InlineData(4, 4, 2048)]
    [InlineData(2048, 2048, 2048)]
    [InlineData(1, 1, 2)]
    public void NoAxisComesBackEmpty(int nx, int ny, int nz)
    {
        var plan = CubeDownsample.For(nx, ny, nz);

        Assert.True(plan.Nx >= 1);
        Assert.True(plan.Ny >= 1);
        Assert.True(plan.Nz >= 1);
    }

    /// <summary>
    /// The sky axes share their stride. They are the same quantity measured two ways, and the slice
    /// view draws the plane as a bitmap stretched uniformly — so striding them apart would change the
    /// shape of the sky.
    /// </summary>
    [Fact]
    public void TheTwoSkyAxesAreStridedTogether()
    {
        var plan = CubeDownsample.For(2048, 512, 16);

        Assert.Equal(8, plan.StrideXY);
        Assert.Equal(256, plan.Nx);
        Assert.Equal(64, plan.Ny);   // 512 / 8, not 512 capped to 256
    }

    /// <summary>
    /// A big isotropic cube is unaffected, which is what makes this safe: the fix only loosens the
    /// stride on axes that never needed it, so the worst case stays MaxDim cubed.
    /// </summary>
    [Fact]
    public void ABigCubeIsStridedExactlyAsBefore()
    {
        var plan = CubeDownsample.For(2048, 2048, 2048);

        Assert.Equal(8, plan.StrideXY);
        Assert.Equal(8, plan.StrideZ);
        Assert.Equal(256, plan.Nx);
        Assert.Equal(256, plan.Nz);
    }

    /// <summary>A stride is a step, so it is never zero or negative however odd the input.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-5)]
    [InlineData(int.MaxValue)]
    public void AStrideIsAlwaysAtLeastOne(int n) => Assert.True(CubeDownsample.StepUnder(n) >= 1);
}
