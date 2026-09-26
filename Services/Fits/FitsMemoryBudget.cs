using System.Runtime.InteropServices;

namespace CanfarDesktop.Services.Fits;

/// <summary>
/// Whether this machine can hold an image's pixels right now, and what to say when it cannot.
///
/// <para>This used to be a fixed 512 MB, which was two answers wrong at once. A workstation with
/// 20 GB free was refused a 1.6 GB MegaPipe tile it could hold with ease; and the same number was
/// the only thing standing between a laptop with 1.5 GB free and swapping itself to a standstill.
/// What decides it is the memory actually free, so that is what is asked.</para>
///
/// <para>The pixels are the only full-size copy the viewer keeps: the file is converted as it is
/// read, and what is drawn is <see cref="FitsDisplayRaster"/>'s picture, bounded whatever the image
/// is. So the budget is the pixels plus a fixed allowance for everything else.</para>
///
/// <para>The one part of FITS loading that asks the operating system anything, kept apart from the
/// parser so the parser's decisions can be tested with any memory the test likes.</para>
/// </summary>
public static class FitsMemoryBudget
{
    /// <summary>
    /// Always allowed, however little is free — the old fixed cap. Every image that opened before
    /// still opens: a full machine can page, and refusing a 30 MB frame because Windows is busy
    /// would be the budget failing people it was meant to protect.
    /// </summary>
    public const long Floor = 512L * 1024 * 1024;

    /// <summary>
    /// Kept back for what else a large image needs once its pixels are in: the display picture and
    /// its two render buffers (each at most <see cref="FitsDisplayRaster.MaxPixels"/> × 4 bytes,
    /// 256 MB), and the app itself.
    /// </summary>
    public const long Headroom = 1024L * 1024 * 1024;

    /// <summary>The most an image's pixels may take, given this much free memory.</summary>
    public static long MaxImageBytes(long availableBytes) => Math.Max(Floor, availableBytes - Headroom);

    /// <summary>
    /// Null when an image of this size fits; otherwise what to tell the person — the whole of what
    /// opening it needs, headroom included. The pixels alone read as "1.5 GB, and 1.5 GB is free",
    /// which is a refusal that looks like a mistake.
    /// </summary>
    /// <param name="pixelBytes">What its pixels take IN MEMORY — 4 bytes each whatever BITPIX says.</param>
    public static string? Refusal(int width, int height, long pixelBytes, long availableBytes)
        => pixelBytes <= MaxImageBytes(availableBytes)
            ? null
            : $"This image is {width:N0} × {height:N0} pixels; opening it needs about " +
              $"{Gigabytes(pixelBytes + Headroom)} of memory, and {Gigabytes(availableBytes)} is free. " +
              "Close other programs and open it again, or download a cutout of the part you need.";

    private static string Gigabytes(long bytes) => $"{bytes / (1024.0 * 1024 * 1024):0.0} GB";

    /// <summary>
    /// Physical memory free right now, standby cache included — what Windows can hand over without
    /// paging anything out.
    /// </summary>
    public static long AvailableBytes()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        try
        {
            if (GlobalMemoryStatusEx(ref status)) return (long)Math.Min(status.AvailPhys, long.MaxValue);
        }
        catch
        {
            // fall through to the GC's view
        }

        // As of the last collection rather than now, but a real figure beats refusing everything.
        var gc = GC.GetGCMemoryInfo();
        return Math.Max(0, gc.TotalAvailableMemoryBytes - gc.MemoryLoadBytes);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
