using System.Numerics;

namespace CanfarDesktop.Services.CubeViewer;

/// <summary>A projected screen point; <see cref="Visible"/> is false when it is behind the camera.</summary>
public readonly record struct CubeScreenPoint(double X, double Y, bool Visible);

/// <summary>
/// Where a point inside the cube lands on the volume view.
///
/// Extracted from the axes overlay when the marks needed it too. Two copies of this arithmetic would
/// not fail loudly — they would agree until someone changed the field of view or the box scaling in one
/// of them, and then a mark would sit slightly off the cube it is supposed to be inside, which reads as
/// the mark being wrong rather than the projection.
///
/// Immutable and free of any rendering API, so both callers build one per frame and it can be tested
/// against known camera positions.
/// </summary>
public sealed class CubeProjector
{
    /// <summary>The field of view the volume renderer draws with. The overlay has to use the same one.</summary>
    public const float FieldOfViewDegrees = 38f;

    private readonly Matrix4x4 _viewProj;
    private readonly float _sx, _sy, _sz;
    private readonly double _width, _height;

    private CubeProjector(Matrix4x4 viewProj, float sx, float sy, float sz, double width, double height)
    {
        _viewProj = viewProj;
        _sx = sx;
        _sy = sy;
        _sz = sz;
        _width = width;
        _height = height;
    }

    /// <summary>
    /// A projector for the current camera and box, or null when the panel has no area to project onto —
    /// which is the state before the first layout, and an agent asking before the window is shown.
    /// </summary>
    public static CubeProjector? Create(
        float azimuth, float elevation, float distance, float spectralScale,
        int volNx, int volNy, double widthDip, double heightDip)
    {
        if (widthDip < 1 || heightDip < 1) return null;

        // The box keeps the cube's aspect on its two spatial axes and takes the spectral scale on the
        // third, so a long thin cube looks long and thin rather than cubic.
        var longest = (float)Math.Max(Math.Max(volNx, volNy), 1);
        var sx = volNx / longest;
        var sy = volNy / longest;

        var eye = CubeMath.OrbitEye(azimuth, elevation, distance);
        var view = CubeMath.LookAt(eye, Vector3.Zero, new Vector3(0, 1, 0));
        var proj = CubeMath.Perspective(FieldOfViewDegrees * MathF.PI / 180f, (float)(widthDip / heightDip), 0.01f, 50f);

        return new CubeProjector(CubeMath.Mul(proj, view), sx, sy, spectralScale, widthDip, heightDip);
    }

    /// <summary>A point in box (model) space, where the cube spans ±0.5 on each axis.</summary>
    public CubeScreenPoint ProjectBox(float bx, float by, float bz)
    {
        var clip = CubeMath.TransformPoint(_viewProj, new Vector4(bx * _sx, by * _sy, bz * _sz, 1f));
        if (clip.W <= 1e-4f) return new CubeScreenPoint(0, 0, false);

        double ndcX = clip.X / clip.W, ndcY = clip.Y / clip.W;
        return new CubeScreenPoint(
            (ndcX * 0.5 + 0.5) * _width,
            (1.0 - (ndcY * 0.5 + 0.5)) * _height,   // clip-space y-up → screen y-down
            true);
    }

    /// <summary>
    /// A voxel, in the cube's own coordinates. The box spans the whole cube, so voxel 0 is at -0.5 and
    /// the last voxel on an axis is at +0.5 — the same convention the axes captions are placed with.
    /// </summary>
    public CubeScreenPoint ProjectVoxel(double x, double y, double channel, int nx, int ny, int nz)
        => ProjectBox(Fraction(x, nx), Fraction(y, ny), Fraction(channel, nz));

    /// <summary>
    /// A voxel index as a box coordinate. An axis one voxel deep has no span to place anything along,
    /// so it collapses to the middle rather than dividing by zero.
    /// </summary>
    private static float Fraction(double index, int count)
        => count > 1 ? (float)(index / (count - 1) - 0.5) : 0f;
}
