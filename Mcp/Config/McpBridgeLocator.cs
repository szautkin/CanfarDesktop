namespace CanfarDesktop.Mcp.Config;

/// <summary>
/// Finds the stdio bridge exe to point Claude at. Searches, in order: next to the running app (the
/// packaged / copied-alongside case), a <c>mcp-bridge</c> subfolder, then — for a dev run from Visual
/// Studio — the sibling bridge project's build output by walking up to the repo root. Returns the first
/// existing path, or null when the bridge hasn't been built/packaged.
/// </summary>
public static class McpBridgeLocator
{
    public const string BridgeExeName = "CanfarDesktop.McpBridge.exe";

    /// <summary>One refresh at a time: the MCP host refreshes on start, and the wizard and settings panel on open.</summary>
    private static readonly object RefreshGate = new();

    public static string? Resolve(string? baseDirectory = null)
    {
        foreach (var candidate in Candidates(baseDirectory ?? AppContext.BaseDirectory))
            if (File.Exists(candidate)) return candidate;
        return null;
    }

    /// <summary>
    /// The path to REGISTER in an external client's config — the client launches it directly, and keeps
    /// it running for as long as it is connected.
    ///
    /// <para>A bridge that ships with the app is never registered where the app keeps it. Installed from
    /// the Store, that is a version-numbered WindowsApps folder: it breaks silently on every update and
    /// is ACL-restricted for other processes. Deployed from Visual Studio, it is the AppX layout folder,
    /// which every deploy deletes — and cannot, while a client is running the bridge from it (DEP0500).
    /// So it is copied to one fixed per-user place, and that is registered, the path AGENTS.md gives
    /// every assistant: <see cref="Helpers.PackagePaths.WritableInteropRoot"/>\Verbinal\mcp-bridge. MSIX
    /// write virtualization would silently sandbox a copy to the real %LOCALAPPDATA%, but the package's
    /// own LocalCache is exempt, real, and launchable by other processes. It survives app updates, not
    /// an uninstall; the same identity puts it back at the same path on reinstall.</para>
    ///
    /// <para>A bridge built by the bridge project itself is returned where it is: devs iterate on it,
    /// and a copy would go stale.</para>
    /// </summary>
    public static string? ResolveStable(string? baseDirectory = null)
    {
        var baseDir = baseDirectory ?? AppContext.BaseDirectory;
        var source = Resolve(baseDir);
        if (source is null || !ShipsWithApp(source, baseDir)) return source;

        return Refresh(source, Path.Combine(
            Helpers.PackagePaths.WritableInteropRoot(), "Verbinal", "mcp-bridge", BridgeExeName));
    }

    /// <summary>
    /// True when <paramref name="bridge"/> is the one packaged with the app — beside it or in its
    /// <c>mcp-bridge</c> folder — rather than the bridge project's own build output.
    /// </summary>
    internal static bool ShipsWithApp(string bridge, string baseDirectory)
    {
        var path = Path.GetFullPath(bridge);
        return Candidates(baseDirectory).Take(2)
            .Any(c => string.Equals(Path.GetFullPath(c), path, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Put <paramref name="source"/> at <paramref name="stable"/> unless the same bridge is already
    /// there, and say which path to register.
    ///
    /// <para>That is <paramref name="stable"/> whenever a bridge is there, fresh or not — an older one
    /// still connects, and a client's config must not be pointed back at a path that is going to
    /// break. <paramref name="source"/> only when nothing could be put there at all.</para>
    ///
    /// <para>The copy at <paramref name="stable"/> is usually running: a client starts its servers when
    /// it starts and keeps them. A running exe cannot be overwritten but can be renamed, so the old
    /// one is moved aside, the new one put in its place, and what was moved aside is deleted once
    /// nothing runs it — on a later refresh, if not this one. The client picks the new bridge up the
    /// next time it starts it. The new copy is written beside the old one first, so the registered
    /// path never holds a half-written exe.</para>
    /// </summary>
    internal static string Refresh(string source, string stable)
    {
        lock (RefreshGate)
        {
            try
            {
                var src = new FileInfo(source);
                var dst = new FileInfo(stable);
                if (!dst.Exists || dst.Length != src.Length || dst.LastWriteTimeUtc < src.LastWriteTimeUtc)
                {
                    Directory.CreateDirectory(dst.DirectoryName!);
                    var incoming = stable + ".new";
                    File.Copy(source, incoming, overwrite: true);
                    if (File.Exists(stable)) File.Move(stable, $"{stable}.old-{Guid.NewGuid():N}");
                    File.Move(incoming, stable);
                }
                SweepAside(stable);
                return stable;
            }
            catch
            {
                // Disk full, an antivirus lock, a folder that cannot be made.
                return File.Exists(stable) ? stable : source;
            }
        }
    }

    /// <summary>Delete the bridges moved aside by earlier refreshes — those still running stay until next time.</summary>
    private static void SweepAside(string stable)
    {
        var dir = Path.GetDirectoryName(stable)!;
        foreach (var old in Directory.EnumerateFiles(dir, Path.GetFileName(stable) + ".old-*"))
        {
            try { File.Delete(old); }
            catch { /* still running */ }
        }
    }

    private static IEnumerable<string> Candidates(string baseDir)
    {
        // Packaged or copied next to the app.
        yield return Path.Combine(baseDir, BridgeExeName);
        yield return Path.Combine(baseDir, "mcp-bridge", BridgeExeName);

        // Dev: sibling project's build output (walk up looking for CanfarDesktop.McpBridge\bin\**).
        var dir = new DirectoryInfo(baseDir);
        for (var i = 0; i < 7 && dir is not null; i++, dir = dir.Parent)
        {
            var bin = Path.Combine(dir.FullName, "CanfarDesktop.McpBridge", "bin");
            if (!Directory.Exists(bin)) continue;
            string? found = null;
            try { found = Directory.GetFiles(bin, BridgeExeName, SearchOption.AllDirectories).FirstOrDefault(); }
            catch { /* ignore unreadable dirs */ }
            if (found is not null) yield return found;
        }
    }
}
