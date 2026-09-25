namespace CanfarDesktop.Helpers;

/// <summary>
/// How big a cube is kept in memory, and which samples are taken.
///
/// <para>Its own file because it was three lines inside the reader and those three lines decided the
/// coordinate space every cube mark lives in. A cube is strided down so the GPU can hold it, and the
/// rendered voxel grid is what an anchor is expressed in — so a choice made to fit a texture also
/// decides where a mark can be put and whether it can be moved at all.</para>
///
/// <para>It used to take ONE stride from the longest of the three axes. A spectral cube's axes are not
/// comparable: an ordinary HARP observation is 4x4 on the sky and 2048 deep, so the cap on the
/// spectral axis chose a stride of 8 and that stride was then applied to a sky plane four samples
/// wide. Both spatial axes came back as a single voxel. The cube rendered as one column with its sky
/// structure discarded, and marks could not be moved at all — a grid one voxel wide has exactly one
/// position, so every drag in the slice view mapped back to (0,0) and every mark piled into the same
/// corner.</para>
/// </summary>
public static class CubeDownsample
{
    /// <summary>The largest any rendered axis may be. The cap the GPU texture has to live inside.</summary>
    public const int MaxDim = 256;

    /// <summary>
    /// The smallest stride that brings an axis of <paramref name="n"/> samples under the cap.
    ///
    /// Measured with the same rounding the kept count uses. Testing <c>n / step</c> — floor — while
    /// the axis actually comes back as <c>CeilDiv(n, step)</c> lets the two disagree by one exactly
    /// when the division is near-exact: a million channels chose a step of 3906 and produced 257
    /// voxels on a 256 cap. Rare, silent, and one voxel over is still over.
    /// </summary>
    public static int StepUnder(int n, int maxDim = MaxDim)
    {
        if (maxDim < 1) maxDim = 1;

        var step = 1;
        while (CeilDiv(n, step) > maxDim) step++;
        return step;
    }

    /// <summary>What a cube of these native dimensions is rendered at, and the strides that get there.</summary>
    /// <param name="StrideXY">
    /// Shared by the two sky axes deliberately. They are the same quantity measured two ways, so
    /// striding them differently would change the shape of the plane — which the slice view draws as a
    /// bitmap and stretches uniformly.
    /// </param>
    public readonly record struct Plan(int Nx, int Ny, int Nz, int StrideXY, int StrideZ);

    /// <summary>
    /// Plan the read: one stride for the sky plane, taken from its own longer axis, and one for the
    /// spectral axis, taken from its own length.
    /// </summary>
    public static Plan For(int nx, int ny, int nz, int maxDim = MaxDim)
    {
        var strideXY = StepUnder(Math.Max(nx, ny), maxDim);
        var strideZ = StepUnder(nz, maxDim);

        return new Plan(
            CeilDiv(nx, strideXY), CeilDiv(ny, strideXY), CeilDiv(nz, strideZ), strideXY, strideZ);
    }

    private static int CeilDiv(int a, int b) => b <= 0 ? a : (a + b - 1) / b;
}
