using CanfarDesktop.Services.Fits;

namespace CanfarDesktop.Services.CubeViewer;

/// <summary>
/// CPU renderer for the 2D slice view: maps one spectral channel of a (normalized) cube
/// through the window + stretch + active colormap into BGRA8 pixels for a WriteableBitmap.
/// Operates on the same normalized <see cref="VolumeData.Data"/> the GPU volume uses, so the
/// slice and volume share window/stretch/colormap. The Windows analogue of the macOS slice path.
/// </summary>
internal static class CubeSliceRenderer
{
    /// <summary>
    /// Render channel <paramref name="channel"/> into <paramref name="destBgra"/>
    /// (length = Nx·Ny·4, BGRA8, opaque). <paramref name="lutRgba"/> is a 256×4 RGBA colormap.
    /// </summary>
    public static void RenderPlane(
        VolumeData vol, int channel, float windowLo, float windowHi,
        ImageStretcher.StretchMode stretch, byte[] lutRgba, byte[] destBgra)
    {
        int nx = vol.Nx, ny = vol.Ny, nz = vol.Nz;
        channel = Math.Clamp(channel, 0, Math.Max(0, nz - 1));
        long planeBase = (long)channel * ny * nx;
        var data = vol.Data;

        for (int y = 0; y < ny; y++)
        {
            int row = y * nx;
            for (int x = 0; x < nx; x++)
            {
                float v = (float)data[planeBase + row + x];          // normalized [0,1]
                float s = ImageStretcher.Stretch(v, windowLo, windowHi, stretch);
                int idx = Math.Clamp((int)(s * 255f + 0.5f), 0, 255);
                int o = idx * 4;                                     // RGBA in the LUT
                int d = (row + x) * 4;                               // BGRA out
                destBgra[d + 0] = lutRgba[o + 2]; // B
                destBgra[d + 1] = lutRgba[o + 1]; // G
                destBgra[d + 2] = lutRgba[o + 0]; // R
                destBgra[d + 3] = 255;            // A
            }
        }
    }

    /// <summary>
    /// Render an already-normalized [0,1] plane (Nx·Ny, row-major) through the window + stretch +
    /// colormap into <paramref name="destBgra"/> (length = Nx·Ny·4, BGRA8, opaque). Used by the
    /// figure export to render a single channel read back at NATIVE FITS resolution (the in-memory
    /// volume is down-sampled), so the exported slice is crisp rather than blocky.
    /// </summary>
    public static void RenderPlaneNorm(
        float[] norm, int nx, int ny, float windowLo, float windowHi,
        ImageStretcher.StretchMode stretch, byte[] lutRgba, byte[] destBgra)
    {
        for (int y = 0; y < ny; y++)
        {
            int row = y * nx;
            for (int x = 0; x < nx; x++)
            {
                float v = norm[row + x];                             // normalized [0,1] (NaN = no-data)
                float s = ImageStretcher.Stretch(v, windowLo, windowHi, stretch);
                int idx = Math.Clamp((int)(s * 255f + 0.5f), 0, 255);
                int o = idx * 4;                                     // RGBA in the LUT
                int d = (row + x) * 4;                               // BGRA out
                destBgra[d + 0] = lutRgba[o + 2]; // B
                destBgra[d + 1] = lutRgba[o + 1]; // G
                destBgra[d + 2] = lutRgba[o + 0]; // R
                destBgra[d + 3] = 255;            // A
            }
        }
    }

    /// <summary>
    /// Extract the spectrum (one value per channel) at spatial pixel (x,y), converted back to
    /// physical data units via the cube's normalization cut. Returns null if out of range.
    /// </summary>
    public static float[]? Spectrum(VolumeData vol, int x, int y, double normLo, double normHi)
    {
        int nx = vol.Nx, ny = vol.Ny, nz = vol.Nz;
        if (x < 0 || y < 0 || x >= nx || y >= ny) return null;
        var data = vol.Data;
        double range = normHi - normLo;
        var spectrum = new float[nz];
        for (int z = 0; z < nz; z++)
        {
            float v = (float)data[((long)z * ny + y) * nx + x]; // normalized [0,1]
            spectrum[z] = (float)(normLo + v * range);          // → physical units
        }
        return spectrum;
    }
}
