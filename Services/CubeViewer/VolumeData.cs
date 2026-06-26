namespace CanfarDesktop.Services.CubeViewer;

/// <summary>
/// A half-precision 3D scalar field ready for upload to a DXGI <c>R16_FLOAT</c>
/// Texture3D. Voxels are stored x-fastest, then y, then z (matching the
/// <c>pz*nx*ny + py*nx + px</c> indexing used by the macOS renderer and the
/// HLSL sampler). Values are normalized to roughly [0, 1] so the shader's
/// window mapping operates directly.
/// </summary>
public sealed class VolumeData
{
    /// <summary>X dimension (voxels).</summary>
    public int Nx { get; }

    /// <summary>Y dimension (voxels).</summary>
    public int Ny { get; }

    /// <summary>Z dimension (voxels / spectral channels).</summary>
    public int Nz { get; }

    /// <summary>Half-float voxel data, length = Nx·Ny·Nz, x-fastest ordering.</summary>
    public Half[] Data { get; }

    /// <summary>A short human-readable label for the cube (file name or "Synthetic …").</summary>
    public string Name { get; }

    public VolumeData(int nx, int ny, int nz, Half[] data, string name)
    {
        Nx = nx;
        Ny = ny;
        Nz = nz;
        Data = data;
        Name = name;
    }

    /// <summary>
    /// Generate a synthetic procedural "nebula" volume: a soft Gaussian core
    /// modulated by multi-octave value noise and a couple of off-center clumps,
    /// so the 3D ray-march has genuine internal structure to orbit around.
    /// </summary>
    /// <param name="size">Edge length of the cube (nx=ny=nz). 128 is a good default.</param>
    /// <param name="seed">RNG seed for reproducible noise.</param>
    /// <remarks>
    /// TODO(FITS ingest): replace this with real NAXIS3 spectral-cube ingest.
    /// A FITS reader should decode the 3D pixel array, compute robust percentile
    /// cut levels (p0.1…p99.9), normalize voxels into [0,1] against those cuts,
    /// down-sample so max(nx,ny,nz) ≤ a GPU cap (Metal used 512; D3D11
    /// Texture3D max is 2048), convert to <see cref="Half"/>, and hand the result
    /// here as a <see cref="VolumeData"/>. The renderer is already format-agnostic.
    /// </remarks>
    public static VolumeData GenerateSyntheticNebula(int size = 128, int seed = 1234)
    {
        int nx = size, ny = size, nz = size;
        var data = new Half[nx * ny * nz];

        // Pre-build a small 3D hash-noise lattice; sampled with trilinear interp
        // at several octaves to get a cloudy fractal field.
        const int lattice = 24;
        var rng = new Random(seed);
        var noise = new float[lattice * lattice * lattice];
        for (int i = 0; i < noise.Length; i++)
            noise[i] = (float)rng.NextDouble();

        float Lerp(float a, float b, float t) => a + (b - a) * t;

        float SampleLattice(float fx, float fy, float fz)
        {
            // Wrap into lattice space.
            fx *= lattice; fy *= lattice; fz *= lattice;
            int x0 = (int)MathF.Floor(fx), y0 = (int)MathF.Floor(fy), z0 = (int)MathF.Floor(fz);
            float tx = fx - x0, ty = fy - y0, tz = fz - z0;
            int X0 = ((x0 % lattice) + lattice) % lattice;
            int Y0 = ((y0 % lattice) + lattice) % lattice;
            int Z0 = ((z0 % lattice) + lattice) % lattice;
            int X1 = (X0 + 1) % lattice, Y1 = (Y0 + 1) % lattice, Z1 = (Z0 + 1) % lattice;

            float N(int x, int y, int z) => noise[(z * lattice + y) * lattice + x];
            float c00 = Lerp(N(X0, Y0, Z0), N(X1, Y0, Z0), tx);
            float c10 = Lerp(N(X0, Y1, Z0), N(X1, Y1, Z0), tx);
            float c01 = Lerp(N(X0, Y0, Z1), N(X1, Y0, Z1), tx);
            float c11 = Lerp(N(X0, Y1, Z1), N(X1, Y1, Z1), tx);
            float c0 = Lerp(c00, c10, ty);
            float c1 = Lerp(c01, c11, ty);
            return Lerp(c0, c1, tz);
        }

        float Fbm(float x, float y, float z)
        {
            float sum = 0f, amp = 0.5f, freq = 1f;
            for (int o = 0; o < 4; o++)
            {
                sum += amp * SampleLattice(x * freq, y * freq, z * freq);
                freq *= 2.07f;
                amp *= 0.5f;
            }
            return sum; // ~[0,1)
        }

        // Two extra emission clumps to break radial symmetry.
        var clumps = new (float cx, float cy, float cz, float r, float w)[]
        {
            (0.33f, 0.40f, 0.55f, 0.16f, 0.9f),
            (0.66f, 0.62f, 0.42f, 0.12f, 0.7f),
        };

        for (int z = 0; z < nz; z++)
        {
            float fz = z / (float)(nz - 1);
            float dz = fz - 0.5f;
            for (int y = 0; y < ny; y++)
            {
                float fy = y / (float)(ny - 1);
                float dy = fy - 0.5f;
                int rowBase = (z * ny + y) * nx;
                for (int x = 0; x < nx; x++)
                {
                    float fx = x / (float)(nx - 1);
                    float dx = fx - 0.5f;

                    // Soft Gaussian core (anisotropic, slightly elongated on Z).
                    float r2 = dx * dx + dy * dy + (dz * dz) * 0.6f;
                    float core = MathF.Exp(-r2 / (2f * 0.045f));

                    // Cloudy structure: fbm modulates the core; a shell ring adds wisps.
                    float cloud = Fbm(fx * 2.3f, fy * 2.3f, fz * 2.3f);
                    float shell = MathF.Exp(-MathF.Pow(MathF.Sqrt(r2) - 0.28f, 2f) / 0.010f);

                    float v = core * (0.45f + 0.85f * cloud) + 0.35f * shell * cloud;

                    foreach (var c in clumps)
                    {
                        float cdx = fx - c.cx, cdy = fy - c.cy, cdz = fz - c.cz;
                        float cr2 = cdx * cdx + cdy * cdy + cdz * cdz;
                        v += c.w * MathF.Exp(-cr2 / (2f * c.r * c.r)) * (0.5f + 0.7f * cloud);
                    }

                    // Mild noise floor so the transfer function has something to cut.
                    v += 0.02f * cloud;

                    v = Math.Clamp(v, 0f, 1.3f);
                    data[rowBase + x] = (Half)v;
                }
            }
        }

        // Normalize to a robust max so the default window [0,1] frames it well.
        float max = 0f;
        for (int i = 0; i < data.Length; i++)
            max = MathF.Max(max, (float)data[i]);
        if (max > 1e-4f)
        {
            float inv = 1f / max;
            for (int i = 0; i < data.Length; i++)
                data[i] = (Half)((float)data[i] * inv);
        }

        return new VolumeData(nx, ny, nz, data, $"Synthetic Nebula {size}³");
    }
}
