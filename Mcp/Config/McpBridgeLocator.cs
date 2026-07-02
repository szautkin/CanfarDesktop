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

    public static string? Resolve(string? baseDirectory = null)
    {
        foreach (var candidate in Candidates(baseDirectory ?? AppContext.BaseDirectory))
            if (File.Exists(candidate)) return candidate;
        return null;
    }

    /// <summary>
    /// The path to REGISTER in an external client's config (Claude Desktop launches it directly).
    /// For a packaged install the in-package path lives under a version-numbered WindowsApps folder —
    /// it breaks silently on every app update and is ACL-restricted for other processes — so the exe
    /// is copied (and refreshed when it changes) to a stable per-user location and THAT path is
    /// returned. A dev-tree bridge is returned as-is: devs iterate on it, a copy would go stale.
    /// </summary>
    public static string? ResolveStable(string? baseDirectory = null)
    {
        var source = Resolve(baseDirectory);
        if (source is null || !RequiresStableCopy(source)) return source;

        var stable = Path.Combine(
            Helpers.PackagePaths.RealLocalAppData(), "Verbinal", "mcp-bridge", BridgeExeName);
        try
        {
            var src = new FileInfo(source);
            var dst = new FileInfo(stable);
            if (!dst.Exists || dst.Length != src.Length || dst.LastWriteTimeUtc < src.LastWriteTimeUtc)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(stable)!);
                File.Copy(source, stable, overwrite: true);
            }
            return stable;
        }
        catch
        {
            // Copy failed (disk full, AV lock) — the in-package path still works until the next update.
            return source;
        }
    }

    /// <summary>True when the resolved exe lives inside a WindowsApps package folder (version-numbered
    /// per update, restricted ACLs) and must be mirrored to a stable path before registering.</summary>
    internal static bool RequiresStableCopy(string path)
        => path.Contains($"{Path.DirectorySeparatorChar}WindowsApps{Path.DirectorySeparatorChar}",
                         StringComparison.OrdinalIgnoreCase);

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
