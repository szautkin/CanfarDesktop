using System.Numerics;

namespace CanfarDesktop.Services.CubeViewer;

/// <summary>
/// Projects the 3D volume box wireframe and the WCS axis captions onto screen
/// (DIP) coordinates, using the identical camera matrices the GPU ray-marcher
/// uses so the overlay aligns pixel-perfect with the rendered volume. The Windows
/// analogue of the macOS <c>CubeAxisCaptions</c> projection.
/// </summary>
internal static class CubeAxesOverlay
{
    /// <summary>A projected screen point; <see cref="Visible"/> is false when behind the camera.</summary>
    public readonly record struct ScreenPoint(double X, double Y, bool Visible);

    /// <summary>A caption label: text, anchor point, and whether it's an axis name (vs an endpoint value).</summary>
    public readonly record struct Caption(string Text, ScreenPoint At, bool IsAxisName);

    public sealed class Frame
    {
        /// <summary>The 12 box edges as projected endpoint pairs.</summary>
        public List<(ScreenPoint A, ScreenPoint B)> Edges { get; } = new(12);

        /// <summary>Axis-name + endpoint-value captions (empty when no metadata).</summary>
        public List<Caption> Captions { get; } = new(9);

        /// <summary>The 4 projected corners of the current-channel slice plane (when <see cref="HasSlicePlane"/>).</summary>
        public ScreenPoint[] SlicePlane { get; } = new ScreenPoint[4];

        /// <summary>True when a slice-plane marker should be drawn (all 4 corners are in front of the camera).</summary>
        public bool HasSlicePlane { get; set; }
    }

    /// <summary>The 8 unit-box corners (model space, ±0.5).</summary>
    private static readonly (float X, float Y, float Z)[] Corners =
    {
        (-.5f,-.5f,-.5f), ( .5f,-.5f,-.5f), ( .5f, .5f,-.5f), (-.5f, .5f,-.5f),
        (-.5f,-.5f, .5f), ( .5f,-.5f, .5f), ( .5f, .5f, .5f), (-.5f, .5f, .5f),
    };

    /// <summary>The 12 edges as corner-index pairs.</summary>
    private static readonly (int, int)[] EdgeIndices =
    {
        (0,1),(1,2),(2,3),(3,0),  // back face (z=-0.5)
        (4,5),(5,6),(6,7),(7,4),  // front face (z=+0.5)
        (0,4),(1,5),(2,6),(3,7),  // connecting edges
    };

    /// <summary>
    /// Fill <paramref name="frame"/> with the projected wireframe + captions for the current
    /// camera and cube. The frame (and its lists) are reused across calls — cleared then
    /// repopulated — so the 60fps render loop avoids per-frame collection allocations.
    /// </summary>
    /// <param name="frame">Reusable output frame (cleared then populated).</param>
    /// <param name="az">Camera azimuth (rad).</param>
    /// <param name="el">Camera elevation (rad).</param>
    /// <param name="dist">Camera distance.</param>
    /// <param name="spectralScale">Z-axis (spectral) box scale.</param>
    /// <param name="volNx">Rendered volume X dimension (for box aspect).</param>
    /// <param name="volNy">Rendered volume Y dimension.</param>
    /// <param name="meta">Cube metadata for the WCS captions (null → wireframe only).</param>
    /// <param name="widthDip">Panel width in DIPs.</param>
    /// <param name="heightDip">Panel height in DIPs.</param>
    /// <param name="sliceFraction">Current channel position (0..1 along the spectral axis) for the
    /// slice-plane marker, or null to omit it.</param>
    public static void Build(
        Frame frame,
        float az, float el, float dist, float spectralScale,
        int volNx, int volNy, CubeMetadata? meta,
        double widthDip, double heightDip,
        float? sliceFraction = null)
    {
        frame.HasSlicePlane = false;
        frame.Edges.Clear();
        frame.Captions.Clear();
        if (widthDip < 1 || heightDip < 1) return;

        float m = Math.Max(volNx, volNy);
        if (m <= 0) m = 1;
        float sx = volNx / m, sy = volNy / m, sz = spectralScale;

        float aspect = (float)(widthDip / heightDip);
        Vector3 eye = CubeMath.OrbitEye(az, el, dist);
        Matrix4x4 view = CubeMath.LookAt(eye, Vector3.Zero, new Vector3(0, 1, 0));
        Matrix4x4 proj = CubeMath.Perspective(38f * MathF.PI / 180f, aspect, 0.01f, 50f);
        Matrix4x4 vp = CubeMath.Mul(proj, view);

        ScreenPoint Project(float bx, float by, float bz)
        {
            // Box (model) coords → world via the diagonal box scale → clip space.
            var clip = CubeMath.TransformPoint(vp, new Vector4(bx * sx, by * sy, bz * sz, 1f));
            if (clip.W <= 1e-4f) return new ScreenPoint(0, 0, false);
            double ndcX = clip.X / clip.W, ndcY = clip.Y / clip.W;
            double px = (ndcX * 0.5 + 0.5) * widthDip;
            double py = (1.0 - (ndcY * 0.5 + 0.5)) * heightDip; // clip-space y-up → screen y-down
            return new ScreenPoint(px, py, true);
        }

        // Box wireframe (scratch corners on the stack — no heap allocation per frame).
        Span<ScreenPoint> pc = stackalloc ScreenPoint[8];
        for (int i = 0; i < 8; i++)
            pc[i] = Project(Corners[i].X, Corners[i].Y, Corners[i].Z);
        foreach (var (a, b) in EdgeIndices)
            frame.Edges.Add((pc[a], pc[b]));

        // Slice-plane marker: a quad across the box at the current channel's spectral depth
        // (model Z = -0.5 + fraction). Lets the user see channel navigation inside the volume.
        if (sliceFraction is { } frac)
        {
            float z = -0.5f + Math.Clamp(frac, 0f, 1f);
            frame.SlicePlane[0] = Project(-0.5f, -0.5f, z);
            frame.SlicePlane[1] = Project(0.5f, -0.5f, z);
            frame.SlicePlane[2] = Project(0.5f, 0.5f, z);
            frame.SlicePlane[3] = Project(-0.5f, 0.5f, z);
            frame.HasSlicePlane = frame.SlicePlane[0].Visible && frame.SlicePlane[1].Visible
                && frame.SlicePlane[2].Visible && frame.SlicePlane[3].Visible;
        }

        // WCS axis captions — positions in box space exactly as the macOS CubeAxisCaptions.
        if (meta is not null)
        {
            var w = meta.Wcs;
            // X axis (longitude): label below-front; endpoints at ±0.5 in X.
            frame.Captions.Add(new Caption(w.LonName, Project(0f, -0.62f, -0.62f), true));
            frame.Captions.Add(new Caption(w.LonText(0), Project(-0.5f, -0.62f, -0.62f), false));
            frame.Captions.Add(new Caption(w.LonText(Math.Max(0, w.Nx - 1)), Project(0.5f, -0.62f, -0.62f), false));
            // Y axis (latitude).
            frame.Captions.Add(new Caption(w.LatName, Project(-0.62f, 0f, -0.62f), true));
            frame.Captions.Add(new Caption(w.LatText(0), Project(-0.62f, -0.5f, -0.62f), false));
            frame.Captions.Add(new Caption(w.LatText(Math.Max(0, w.Ny - 1)), Project(-0.62f, 0.5f, -0.62f), false));
            // Z axis (spectral).
            string zName = (w.SpecAxisName() + " " + w.SpecUnitDisplay()).Trim();
            frame.Captions.Add(new Caption(zName, Project(-0.62f, -0.62f, 0f), true));
            frame.Captions.Add(new Caption(w.SpecText(0), Project(-0.62f, -0.62f, -0.5f), false));
            frame.Captions.Add(new Caption(w.SpecText(Math.Max(0, w.Nz - 1)), Project(-0.62f, -0.62f, 0.5f), false));
        }
    }
}
