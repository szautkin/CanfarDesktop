using System.Numerics;

namespace CanfarDesktop.Services.CubeViewer;

/// <summary>
/// Column-major 4x4 matrix helpers for the cube volume camera. Ported from the
/// macOS <c>makePerspective</c>/<c>makeLookAt</c> in CubeVolumeRenderer.swift.
/// </summary>
/// <remarks>
/// <para>
/// All matrices are stored as the <em>true mathematical</em> matrix inside a
/// <see cref="Matrix4x4"/> (where the <c>M{row}{col}</c> fields literally hold
/// element row,col). <see cref="Mul"/> and <see cref="Invert"/> operate on that
/// convention, and the HLSL shader reads the constant buffer with
/// <c>#pragma pack_matrix(row_major)</c> + <c>mul(matrix, vector)</c>, so the
/// math matches the Metal <c>matrix * column-vector</c> source exactly with no
/// transpose anywhere in the pipeline.
/// </para>
/// <para>
/// Both Metal and Direct3D use a clip-space depth range of z ∈ [0, 1], so the
/// projection is identical to the Metal original.
/// </para>
/// </remarks>
internal static class CubeMath
{
    /// <summary>
    /// Build a matrix from four <em>column</em> vectors (the way the Metal
    /// source specifies its matrices), assigning each column to the correct
    /// <c>M{row}{col}</c> fields so the result is the true mathematical matrix.
    /// </summary>
    private static Matrix4x4 FromColumns(Vector4 c0, Vector4 c1, Vector4 c2, Vector4 c3) => new(
        c0.X, c1.X, c2.X, c3.X, // row 0  (M11..M14)
        c0.Y, c1.Y, c2.Y, c3.Y, // row 1
        c0.Z, c1.Z, c2.Z, c3.Z, // row 2
        c0.W, c1.W, c2.W, c3.W); // row 3

    /// <summary>
    /// Right-handed perspective projection (column-vector convention, clip z ∈ [0, 1]).
    /// Direct port of the Swift <c>makePerspective</c>.
    /// </summary>
    /// <param name="fovYRadians">Vertical field of view in radians.</param>
    /// <param name="aspect">Viewport width / height.</param>
    /// <param name="near">Near clip distance (&gt; 0).</param>
    /// <param name="far">Far clip distance.</param>
    public static Matrix4x4 Perspective(float fovYRadians, float aspect, float near, float far)
    {
        float ys = 1f / MathF.Tan(fovYRadians * 0.5f);
        float xs = ys / MathF.Max(aspect, 0.0001f);
        float zs = far / (near - far);

        // Metal columns:
        //  col0=(xs,0,0,0) col1=(0,ys,0,0) col2=(0,0,zs,-1) col3=(0,0,zs*near,0)
        return FromColumns(
            new Vector4(xs, 0f, 0f, 0f),
            new Vector4(0f, ys, 0f, 0f),
            new Vector4(0f, 0f, zs, -1f),
            new Vector4(0f, 0f, zs * near, 0f));
    }

    /// <summary>
    /// Right-handed look-at view matrix (column-vector convention). Direct port
    /// of the Swift <c>makeLookAt</c>.
    /// </summary>
    public static Matrix4x4 LookAt(Vector3 eye, Vector3 center, Vector3 up)
    {
        Vector3 z = Vector3.Normalize(eye - center);
        Vector3 x = Vector3.Normalize(Vector3.Cross(up, z));
        Vector3 y = Vector3.Cross(z, x);

        // Metal columns:
        //  col0=(x.x,y.x,z.x,0) col1=(x.y,y.y,z.y,0)
        //  col2=(x.z,y.z,z.z,0) col3=(-dot(x,eye),-dot(y,eye),-dot(z,eye),1)
        return FromColumns(
            new Vector4(x.X, y.X, z.X, 0f),
            new Vector4(x.Y, y.Y, z.Y, 0f),
            new Vector4(x.Z, y.Z, z.Z, 0f),
            new Vector4(-Vector3.Dot(x, eye), -Vector3.Dot(y, eye), -Vector3.Dot(z, eye), 1f));
    }

    /// <summary>Diagonal scale matrix (the cube model matrix = spatial/spectral scale).</summary>
    public static Matrix4x4 Scale(float sx, float sy, float sz) => new(
        sx, 0f, 0f, 0f,
        0f, sy, 0f, 0f,
        0f, 0f, sz, 0f,
        0f, 0f, 0f, 1f);

    /// <summary>
    /// Multiply two true-math matrices: <c>result = a · b</c> (so that
    /// <c>result · v = a · (b · v)</c>), matching the Swift <c>proj * view *
    /// model</c> ordering. Computed element-wise to avoid System.Numerics'
    /// row-vector multiply convention.
    /// </summary>
    public static Matrix4x4 Mul(Matrix4x4 a, Matrix4x4 b)
    {
        Matrix4x4 r = default;
        r.M11 = a.M11 * b.M11 + a.M12 * b.M21 + a.M13 * b.M31 + a.M14 * b.M41;
        r.M12 = a.M11 * b.M12 + a.M12 * b.M22 + a.M13 * b.M32 + a.M14 * b.M42;
        r.M13 = a.M11 * b.M13 + a.M12 * b.M23 + a.M13 * b.M33 + a.M14 * b.M43;
        r.M14 = a.M11 * b.M14 + a.M12 * b.M24 + a.M13 * b.M34 + a.M14 * b.M44;

        r.M21 = a.M21 * b.M11 + a.M22 * b.M21 + a.M23 * b.M31 + a.M24 * b.M41;
        r.M22 = a.M21 * b.M12 + a.M22 * b.M22 + a.M23 * b.M32 + a.M24 * b.M42;
        r.M23 = a.M21 * b.M13 + a.M22 * b.M23 + a.M23 * b.M33 + a.M24 * b.M43;
        r.M24 = a.M21 * b.M14 + a.M22 * b.M24 + a.M23 * b.M34 + a.M24 * b.M44;

        r.M31 = a.M31 * b.M11 + a.M32 * b.M21 + a.M33 * b.M31 + a.M34 * b.M41;
        r.M32 = a.M31 * b.M12 + a.M32 * b.M22 + a.M33 * b.M32 + a.M34 * b.M42;
        r.M33 = a.M31 * b.M13 + a.M32 * b.M23 + a.M33 * b.M33 + a.M34 * b.M43;
        r.M34 = a.M31 * b.M14 + a.M32 * b.M24 + a.M33 * b.M34 + a.M34 * b.M44;

        r.M41 = a.M41 * b.M11 + a.M42 * b.M21 + a.M43 * b.M31 + a.M44 * b.M41;
        r.M42 = a.M41 * b.M12 + a.M42 * b.M22 + a.M43 * b.M32 + a.M44 * b.M42;
        r.M43 = a.M41 * b.M13 + a.M42 * b.M23 + a.M43 * b.M33 + a.M44 * b.M43;
        r.M44 = a.M41 * b.M14 + a.M42 * b.M24 + a.M43 * b.M34 + a.M44 * b.M44;
        return r;
    }

    /// <summary>
    /// Invert a true-math matrix; falls back to identity if singular (degenerate
    /// camera). <see cref="Matrix4x4.Invert"/> is convention-agnostic — it
    /// computes the genuine matrix inverse — so it is correct here.
    /// </summary>
    public static Matrix4x4 Invert(Matrix4x4 m) =>
        Matrix4x4.Invert(m, out var inv) ? inv : Matrix4x4.Identity;

    /// <summary>
    /// Camera eye position for an orbit camera. Port of the Swift
    /// <c>cameraPosition()</c>: eye = d·(cosEl·sinAz, sinEl, cosEl·cosAz).
    /// </summary>
    public static Vector3 OrbitEye(float azimuth, float elevation, float distance)
    {
        float ce = MathF.Cos(elevation), se = MathF.Sin(elevation);
        return new Vector3(
            distance * ce * MathF.Sin(azimuth),
            distance * se,
            distance * ce * MathF.Cos(azimuth));
    }
}
