using System.Runtime.InteropServices;

namespace CanfarDesktop.Helpers;

/// <summary>
/// Resolves the REAL (un-redirected) per-user AppData folders. For an MSIX-packaged app, the shell
/// redirects FOLDERID_LocalAppData / FOLDERID_RoamingAppData to the package's
/// <c>…\Packages\&lt;PFN&gt;\LocalCache\…</c> sandbox, so <see cref="Environment.GetFolderPath"/> (and
/// thus a plain write) lands somewhere an UNPACKAGED process (the MCP bridge) and OTHER packaged apps
/// (Claude Desktop) can't see. Passing <c>KF_FLAG_NO_APPCONTAINER_REDIRECTION</c> to
/// <c>SHGetKnownFolderPath</c> returns the real path; writing there is not redirected.
/// </summary>
public static class PackagePaths
{
    private const uint KF_FLAG_NO_APPCONTAINER_REDIRECTION = 0x00010000;

    private static readonly Guid FOLDERID_LocalAppData = new("F1B32785-6FBA-4FCF-9D55-7B8E7F157091");
    private static readonly Guid FOLDERID_RoamingAppData = new("3EB685DB-65F9-4CF6-A03A-E3EF65729F3D");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = false)]
    private static extern void SHGetKnownFolderPath(in Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

    /// <summary>The real <c>%LOCALAPPDATA%</c> (un-redirected), or the redirected one if the call fails.</summary>
    public static string RealLocalAppData()
        => Resolve(FOLDERID_LocalAppData) ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>The real <c>%APPDATA%</c> (un-redirected), or the redirected one if the call fails.</summary>
    public static string RealRoamingAppData()
        => Resolve(FOLDERID_RoamingAppData) ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

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
