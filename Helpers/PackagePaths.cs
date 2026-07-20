using System.Runtime.InteropServices;

namespace CanfarDesktop.Helpers;

/// <summary>
/// MSIX path handling. Two DISTINCT problems, and this class addresses both without any restricted
/// capability (this app ships through the Microsoft Store, so <c>unvirtualizedResources</c> is out):
///
/// 1. PATH LOOKUP — for an AppContainer process the shell redirects FOLDERID_LocalAppData /
///    FOLDERID_RoamingAppData into the package sandbox. <see cref="RealLocalAppData"/> /
///    <see cref="RealRoamingAppData"/> (SHGetKnownFolderPath + KF_FLAG_NO_APPCONTAINER_REDIRECTION)
///    return the real per-user paths. Use these whenever a path STRING is read from or handed to
///    another process (locating Claude's config, registering an exe path, detection heuristics).
///
/// 2. WRITE VIRTUALIZATION — a packaged app's writes under AppData are copy-on-write redirected into
///    <c>…\Packages\&lt;PFN&gt;\LocalCache\Local|Roaming</c> EVEN WHEN the write targets the real path.
///    The app reads its own writes back through a merged view (so everything looks fine), but external
///    processes see nothing (verified empirically). No path choice under AppData escapes this — except
///    the <c>%LOCALAPPDATA%\Packages</c> tree itself, which is exempt. So files another process must
///    read are written under <see cref="WritableInteropRoot"/> (the package's own LocalCache — a real,
///    externally readable location), and writes whose target <see cref="IsWriteVirtualized"/> (e.g. a
///    traditional-install Claude config in real %APPDATA%) must be routed through the user instead.
/// </summary>
public static class PackagePaths
{
    private const uint KF_FLAG_NO_APPCONTAINER_REDIRECTION = 0x00010000;
    private const int APPMODEL_ERROR_NO_PACKAGE = 15700;

    private static readonly Guid FOLDERID_LocalAppData = new("F1B32785-6FBA-4FCF-9D55-7B8E7F157091");
    private static readonly Guid FOLDERID_RoamingAppData = new("3EB685DB-65F9-4CF6-A03A-E3EF65729F3D");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = false)]
    private static extern void SHGetKnownFolderPath(in Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetCurrentPackageFamilyName(ref uint packageFamilyNameLength, char[]? packageFamilyName);

    /// <summary>The real <c>%LOCALAPPDATA%</c> (un-redirected), or the redirected one if the call fails.</summary>
    public static string RealLocalAppData()
        => Resolve(FOLDERID_LocalAppData) ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>The real <c>%APPDATA%</c> (un-redirected), or the redirected one if the call fails.</summary>
    public static string RealRoamingAppData()
        => Resolve(FOLDERID_RoamingAppData) ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    /// <summary>
    /// The package family name of the current process, or null when running unpackaged.
    /// Kernel32-only (no WinRT), so linked-source consumers (the bridge, tests) can call it too.
    /// </summary>
    public static string? CurrentPackageFamilyName()
    {
        try
        {
            uint length = 0;
            var rc = GetCurrentPackageFamilyName(ref length, null);
            if (rc == APPMODEL_ERROR_NO_PACKAGE || length == 0) return null;
            var buffer = new char[length];
            rc = GetCurrentPackageFamilyName(ref length, buffer);
            return rc == 0 && length > 1 ? new string(buffer, 0, (int)length - 1) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// A per-user directory whose writes land at their literal on-disk path even under MSIX write
    /// virtualization, and which external processes can read: the package's own
    /// <c>…\Packages\&lt;PFN&gt;\LocalCache</c> when packaged (the Packages tree is exempt from
    /// virtualization), else the real <c>%LOCALAPPDATA%</c>. Contents survive app updates but are
    /// deleted on uninstall — anything registered elsewhere by absolute path (e.g. the bridge exe in
    /// Claude's config) must be re-registered after a reinstall.
    /// </summary>
    public static string WritableInteropRoot()
    {
        var pfn = CurrentPackageFamilyName();
        return pfn is null
            ? RealLocalAppData()
            : Path.Combine(RealLocalAppData(), "Packages", pfn, "LocalCache");
    }

    /// <summary>
    /// True when a write by THIS process to <paramref name="path"/> would be copy-on-write redirected
    /// into the package sandbox (i.e. invisible to every other process). Decide this BEFORE writing —
    /// a write-then-read-back check would falsely succeed, because the app reads the merged view.
    /// </summary>
    public static bool IsWriteVirtualized(string path)
    {
        if (CurrentPackageFamilyName() is null) return false;

        string full;
        try { full = Path.GetFullPath(path); }
        catch { return false; }

        var local = RealLocalAppData();
        if (IsUnder(full, Path.Combine(local, "Packages"))) return false; // exempt tree
        return IsUnder(full, local) || IsUnder(full, RealRoamingAppData());
    }

    private static bool IsUnder(string path, string root)
        => path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string? Resolve(Guid folderId)
    {
        var ptr = IntPtr.Zero;
        try
        {
            SHGetKnownFolderPath(folderId, KF_FLAG_NO_APPCONTAINER_REDIRECTION, IntPtr.Zero, out ptr);
            return ptr != IntPtr.Zero ? Marshal.PtrToStringUni(ptr) : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (ptr != IntPtr.Zero) Marshal.FreeCoTaskMem(ptr);
        }
    }
}
