namespace CanfarDesktop.Services.CubeViewer;

/// <summary>
/// Per-channel mean profile of the (down-sampled) cube — the waveform backdrop the channel
/// scrubber draws so the user can see where the signal lives along the spectral axis. The
/// Windows analogue of the macOS <c>CubeModel.channelMeans()</c> + <c>ChannelScrubber</c>
/// waveform math. Pure (no WinUI) so it is unit-testable.
/// </summary>
internal static class CubeChannelProfile
{
    /// <summary>
    /// NaN-aware mean of each z-plane (x-fastest layout, matching <see cref="VolumeData.Data"/>).
    /// Returns null when there is no scrubbable spectral axis (nz &lt; 2) or the dims are empty.
    /// An all-NaN channel yields NaN (drawn at the baseline by <see cref="NormalizedHeights"/>).
    /// </summary>
    public static float[]? Compute(Half[] data, int nx, int ny, int nz)
    {
        long planeVox = (long)nx * ny;
        if (nz < 2 || planeVox < 1 || data.Length < planeVox * nz) return null;

        var means = new float[nz];
        for (int z = 0; z < nz; z++)
        {
            long baseIdx = z * planeVox;
            double sum = 0;
            long finite = 0;
            for (long i = 0; i < planeVox; i++)
            {
                float v = (float)data[baseIdx + i];
                if (float.IsNaN(v) || float.IsInfinity(v)) continue;
                sum += v;
                finite++;
            }
            means[z] = finite > 0 ? (float)(sum / finite) : float.NaN;
        }
        return means;
    }

    /// <summary>
    /// Map a profile to [0,1] waveform heights against its finite min/max (non-finite → 0,
    /// matching the macOS scrubber; a flat profile maps to 0 so it draws as a baseline).
    /// </summary>
    public static float[] NormalizedHeights(IReadOnlyList<float> profile)
    {
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (var v in profile)
            if (float.IsFinite(v)) { if (v < lo) lo = v; if (v > hi) hi = v; }
        float range = hi > lo ? hi - lo : 1f;
        if (lo > hi) lo = 0f; // no finite values at all → everything maps to 0

        var heights = new float[profile.Count];
        for (int i = 0; i < profile.Count; i++)
            heights[i] = float.IsFinite(profile[i]) ? Math.Clamp((profile[i] - lo) / range, 0f, 1f) : 0f;
        return heights;
    }
}
