using System.Buffers.Binary;
using CanfarDesktop.Services.Fits;

namespace CanfarDesktop.Services.CubeViewer;

/// <summary>
/// Reads a 3D FITS spectral cube (NAXIS=3) into a <see cref="VolumeData"/> ready for the GPU volume
/// renderer: decodes the BITPIX pixel array (8/16/32/-32/-64, big-endian, with BSCALE/BZERO), strides
/// it down so the longest axis fits a GPU-friendly cap, normalizes against robust percentile cut levels
/// into [0,1], and converts to <see cref="Half"/>. The Windows analogue of the macOS cube ingest.
/// </summary>
internal static class FitsCubeReader
{
    /// <summary>Down-sample so max(nx,ny,nz) ≤ this (keeps the Texture3D + RAM budget sane).</summary>
    private const int MaxDim = 256;

    public static VolumeData Read(string path)
    {
        using var stream = FitsContainer.OpenFits(path);
        var header = FitsParser.ReadHeader(stream)
            ?? throw new InvalidDataException("No FITS header found.");

        int naxis = header.NAxis;
        int nx = header.NAxis1, ny = header.NAxis2, nz = header.GetInt("NAXIS3", 1);
        if (naxis < 3 || nx < 1 || ny < 1 || nz < 2)
            throw new InvalidDataException($"Not a 3D FITS cube (NAXIS={naxis}, dims {nx}×{ny}×{nz}).");

        int bitpix = header.BitPix;
        int bytesPerSample = Math.Abs(bitpix) / 8;
        if (bytesPerSample == 0)
            throw new InvalidDataException($"Unsupported BITPIX {bitpix}.");
        double bscale = header.BScale, bzero = header.BZero;

        // Stride so the longest axis fits the cap.
        int step = 1;
        while (Math.Max(nx, Math.Max(ny, nz)) / step > MaxDim) step++;
        int onx = CeilDiv(nx, step), ony = CeilDiv(ny, step), onz = CeilDiv(nz, step);

        var outData = new float[(long)onx * ony * onz];
        var plane = new byte[(long)nx * ny * bytesPerSample];

        // Exact value statistics accumulated over EVERY voxel (every plane is read anyway),
        // so the info-panel RANGE / NaN% are the true cube extrema, not a strided estimate.
        double gmin = double.PositiveInfinity, gmax = double.NegativeInfinity;
        long nan = 0;
        long totalVox = (long)nx * ny * nz;
        int planeVox = nx * ny;

        int oz = 0;
        for (int z = 0; z < nz; z++)
        {
            ReadFull(stream, plane);          // every plane is read (data is contiguous); kept ones strided

            // Full-plane stats pass.
            for (int i = 0; i < planeVox; i++)
            {
                float v = Decode(plane, i * bytesPerSample, bitpix, bscale, bzero);
                if (float.IsNaN(v) || float.IsInfinity(v)) { nan++; continue; }
                if (v < gmin) gmin = v;
                if (v > gmax) gmax = v;
            }

            if (z % step != 0) continue;
            int oy = 0;
            for (int y = 0; y < ny; y += step, oy++)
            {
                long rowOff = (long)y * nx;
                int ox = 0;
                for (int x = 0; x < nx; x += step, ox++)
                {
                    float v = Decode(plane, (int)((rowOff + x) * bytesPerSample), bitpix, bscale, bzero);
                    outData[((long)oz * ony + oy) * onx + ox] = v;
                }
            }
            oz++;
        }

        if (gmin > gmax) { gmin = 0; gmax = 1; } // all-NaN edge case

        // Sorted finite sample (from the strided voxels) → median + the p0.5/p99.5 cut used
        // for both display normalization and the colorbar value range. Sorted once here.
        var finite = new List<float>(outData.Length);
        foreach (var v in outData)
            if (!float.IsNaN(v) && !float.IsInfinity(v)) finite.Add(v);
        double median = 0;
        float normLo = (float)gmin, normHi = (float)gmax;
        if (finite.Count > 0)
        {
            finite.Sort();
            median = finite[finite.Count / 2];
            normLo = finite[(int)(finite.Count * 0.005f)];
            normHi = finite[Math.Min(finite.Count - 1, (int)(finite.Count * 0.995f))];
        }

        var meta = BuildMetadata(header, nx, ny, nz, onx, ony, oz,
                                 gmin, gmax, median, (double)nan / Math.Max(1, totalVox),
                                 normLo, normHi, path);

        Normalize(outData, normLo, normHi);
        var half = new Half[outData.Length];
        for (int i = 0; i < outData.Length; i++) half[i] = (Half)outData[i];

        return new VolumeData(onx, ony, oz, half, Path.GetFileNameWithoutExtension(path), meta);
    }

    /// <summary>
    /// Build the display metadata. Min/max/NaN are the EXACT full-cube stats; the median + the
    /// p0.5/p99.5 normalization cut come from the strided sample (sorted once by the caller).
    /// </summary>
    private static CubeMetadata BuildMetadata(
        Models.Fits.FitsHeader header,
        int nx, int ny, int nz, int rnx, int rny, int rnz,
        double min, double max, double median, double nanFraction,
        double normLo, double normHi, string path)
    {
        var telescope = (header.GetString("TELESCOP") ?? "").Trim();
        var instrument = (header.GetString("INSTRUME") ?? "").Trim();
        var instr = string.Join(" · ", new[] { telescope, instrument }.Where(s => s.Length > 0));

        return new CubeMetadata
        {
            Object = (header.GetString("OBJECT") ?? "").Trim(),
            Instrument = instr,
            Bunit = (header.GetString("BUNIT") ?? "").Trim(),
            Nx = nx, Ny = ny, Nz = nz,
            RenderNx = rnx, RenderNy = rny, RenderNz = rnz,
            DataMin = min, DataMax = max, Median = median,
            NormLo = normLo, NormHi = normHi,
            NanFraction = nanFraction,
            Wcs = CubeWcs.FromHeader(header, nx, ny, nz),
        };
    }

    /// <summary>Normalize physical values into [0,1] against the given cut levels; NaN/Inf → 0.</summary>
    private static void Normalize(float[] data, float lo, float hi)
    {
        float range = hi > lo ? hi - lo : 1f;
        for (int i = 0; i < data.Length; i++)
        {
            float v = data[i];
            data[i] = (float.IsNaN(v) || float.IsInfinity(v))
                ? 0f
                : Math.Clamp((v - lo) / range, 0f, 1f);
        }
    }

    private static float Decode(byte[] buf, int off, int bitpix, double bscale, double bzero)
    {
        double raw = bitpix switch
        {
            -32 => BinaryPrimitives.ReadSingleBigEndian(buf.AsSpan(off)),
            -64 => BinaryPrimitives.ReadDoubleBigEndian(buf.AsSpan(off)),
            16 => BinaryPrimitives.ReadInt16BigEndian(buf.AsSpan(off)),
            32 => BinaryPrimitives.ReadInt32BigEndian(buf.AsSpan(off)),
            8 => buf[off],
            _ => 0.0,
        };
        return (float)(bzero + bscale * raw);
    }

    private static int CeilDiv(int a, int b) => (a + b - 1) / b;

    private static void ReadFull(Stream s, byte[] buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int n = s.Read(buffer, total, buffer.Length - total);
            if (n == 0) throw new EndOfStreamException("FITS cube data ended early.");
            total += n;
        }
    }
}
