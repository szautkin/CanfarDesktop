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
