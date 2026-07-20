namespace CanfarDesktop.Services.CubeViewer;

/// <summary>
/// Extracts the spectrum at a spaxel for <c>probe_cube_spectrum</c>. Pure (no UI types) so the
/// QA-critical behaviours are unit-testable: coordinates are NATIVE cube pixels (the volume is
/// down-sampled to the GPU cap, so they are mapped through the stride — QA F10 probed a 720×360
/// cube whose in-memory volume was 240×120 and every valid pixel read as "out of range");
/// blanked voxels (NaN/Inf) become null flux entries (raw NaN crashed the JSON serializer);
/// and the spectral axis is evaluated at NATIVE channels so a z-strided cube reports correct
/// world values. Failure modes are typed, not collapsed to null.
/// </summary>
public static class CubeSpectrumProber
{
    public static CubeSpectrumProbe Probe(VolumeData? vol, int x, int y)
    {
        if (vol is null) return new(CubeProbeStatus.NoCube, null);

        var meta = vol.Meta;
        int nx = meta?.Nx ?? vol.Nx, ny = meta?.Ny ?? vol.Ny;   // native dims (= volume dims when no meta)
        if (x < 0 || y < 0 || x >= nx || y >= ny)
            return new(CubeProbeStatus.OutOfRange, null, nx, ny);

        // Native pixel → volume voxel: the reader keeps every Stride-th sample, so the sample
        // at-or-below the probed pixel is floor(x / stride) (clamped for safety at the far edge).
        int stride = Math.Max(1, meta?.Stride ?? 1);
        int vx = Math.Min(x / stride, vol.Nx - 1);
        int vy = Math.Min(y / stride, vol.Ny - 1);

        var flux = CubeSliceRenderer.Spectrum(vol, vx, vy, meta?.NormLo ?? 0, meta?.NormHi ?? 1);
        if (flux is null) return new(CubeProbeStatus.OutOfRange, null, nx, ny); // unreachable by construction

        int nz = flux.Length;
        var axis = new double[nz];
        var fl = new double?[nz];
        int blanked = 0;
        bool hasSpec = meta?.Wcs.HasSpectral == true;
        for (int z = 0; z < nz; z++)
        {
            int nativeZ = meta?.NativeChannel(z) ?? z;
            double a = hasSpec ? meta!.Wcs.SpectralValue(nativeZ) : nativeZ;
            axis[z] = double.IsFinite(a) ? a : nativeZ;          // a garbage spectral WCS must not poison the JSON
            float v = flux[z];
            if (float.IsFinite(v)) fl[z] = v;
            else { fl[z] = null; blanked++; }
        }

        var w = meta?.Wcs;
        var result = new CubeSpectrumResult(x, y, axis, fl, meta?.Bunit ?? "",
            hasSpec ? w!.SpecUnitDisplay() : "channel",
            SpectralFrame: string.IsNullOrEmpty(w?.SpectralFrame) ? null : w!.SpectralFrame,
            RestFrequencyGHz: w?.RestFrequencyGHz,
            BeamMajorArcsec: w?.BeamMajorDeg is double bmaj ? bmaj * 3600.0 : null,
            BeamMinorArcsec: w?.BeamMinorDeg is double bmin ? bmin * 3600.0 : null,
            BeamPaDeg: w?.BeamPaDeg,
            BlankedChannels: blanked);
        return new(CubeProbeStatus.Ok, result, nx, ny);
    }
}
