using CanfarDesktop.Services.Fits;

namespace CanfarDesktop.Services.CubeViewer;

/// <summary>
/// A persistent, seek-based reader for one cube's spectral planes at NATIVE resolution. Opened once
/// per loaded cube and kept for its lifetime, it serves any channel by seeking straight to that plane
/// — used by the in-app slice view (so the on-screen slice is full-res, not the GPU-down-sampled
/// volume) and by the figure export.
///
/// Restricted to plain, seekable, on-disk FITS: a gzip/tar container decompresses to an in-memory
/// stream, so keeping it would mean holding the whole native cube in RAM for the tab's lifetime —
/// exactly what the volume down-sample exists to avoid. Those cubes fall back to the down-sampled
/// slice in-app (and a one-off native read for export). Very large planes are skipped for the same
/// reason (the reused per-plane buffers would be too big to hold).
/// </summary>
internal sealed class NativeSliceSource : IDisposable
{
    /// <summary>Skip cubes whose single native plane exceeds this (held buffers would be too large).</summary>
    private const long MaxPlaneBytes = 64L * 1024 * 1024; // ~4096² float

    private Stream? _stream;
    private readonly long _dataStart;
    private readonly int _bitpix, _bpp;
    private readonly double _bscale, _bzero;
    private readonly byte[] _planeBuf; // reused across reads (dims are fixed)
    private readonly float[] _norm;    // reused; valid only until the next ReadChannel call

    public int Nx { get; }
    public int Ny { get; }
    public int Nz { get; }

    private NativeSliceSource(Stream s, long dataStart, int nx, int ny, int nz, int bitpix, double bscale, double bzero)
    {
        _stream = s;
        _dataStart = dataStart;
        Nx = nx; Ny = ny; Nz = nz;
        _bitpix = bitpix; _bpp = Math.Abs(bitpix) / 8;
        _bscale = bscale; _bzero = bzero;
        _planeBuf = new byte[(long)nx * ny * _bpp];
        _norm = new float[(long)nx * ny];
    }

    /// <summary>
    /// Open a persistent native-plane source for <paramref name="path"/>, or null when the file is not
    /// a plain, seekable, on-disk FITS cube with a modest plane size (caller falls back to down-sampled).
    /// </summary>
    public static NativeSliceSource? TryOpen(string path)
    {
        FileStream? fs = null;
        try
        {
            // Hold the handle for the tab's lifetime, but share generously: ReadWrite | Delete means
            // this open NEVER blocks a concurrent download from overwriting, renaming, or deleting the
            // file (a default FileShare.Read would lock it and make the writer throw IOException).
            fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.RandomAccess);
            // Plain FITS only: the primary header begins "SIMPLE". A gzip/tar wrapper would force the
            // whole cube into RAM, so skip it (the down-sampled slice is used in-app instead).
            Span<byte> magic = stackalloc byte[6];
            if (fs.Read(magic) < 6 || !magic.SequenceEqual("SIMPLE"u8)) { fs.Dispose(); return null; }
            fs.Position = 0;

            var header = FitsParser.ReadHeader(fs);
            if (header is null) { fs.Dispose(); return null; }

            int nx = header.NAxis1, ny = header.NAxis2, nz = header.GetInt("NAXIS3", 1);
            int bpp = Math.Abs(header.BitPix) / 8;
            if (nx < 1 || ny < 1 || nz < 1 || bpp == 0 || (long)nx * ny * bpp > MaxPlaneBytes)
            {
                fs.Dispose();
                return null;
            }
            // After ReadHeader the stream sits at the first data block (header read in 2880-byte blocks).
            return new NativeSliceSource(fs, fs.Position, nx, ny, nz, header.BitPix, header.BScale, header.BZero);
        }
        catch
        {
            fs?.Dispose();
            return null;
        }
    }

    /// <summary>
    /// Read native channel <paramref name="channel"/> and normalize to [0,1] against the cube's cut
    /// (NaN/Inf kept as NaN). The returned array is a REUSED buffer — valid only until the next call,
    /// so consume it immediately. Returns null on a bad channel or an I/O error.
    /// </summary>
    public (float[] Norm, int Nx, int Ny)? ReadChannel(int channel, double normLo, double normHi)
    {
        if (_stream is null || channel < 0 || channel >= Nz) return null;
        try
        {
            _stream.Seek(_dataStart + (long)_planeBuf.Length * channel, SeekOrigin.Begin);
            _stream.ReadExactly(_planeBuf, 0, _planeBuf.Length);
        }
        catch
        {
            return null;
        }

        float lo = (float)normLo, hi = (float)normHi;
        float range = hi > lo ? hi - lo : 1f;
        int count = Nx * Ny;
        for (int i = 0; i < count; i++)
        {
            float v = FitsCubeReader.Decode(_planeBuf, i * _bpp, _bitpix, _bscale, _bzero);
            _norm[i] = (float.IsNaN(v) || float.IsInfinity(v))
                ? float.NaN
                : Math.Clamp((v - lo) / range, 0f, 1f);
        }
        return (_norm, Nx, Ny);
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;
    }
}
