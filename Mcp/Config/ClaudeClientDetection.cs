using CanfarDesktop.Helpers;

namespace CanfarDesktop.Mcp.Config;

/// <summary>Which Claude clients look installed, so the wizard can tailor its "detected / not detected" cards.</summary>
public sealed record ClaudeClients(bool DesktopInstalled, bool CodeInstalled);

/// <summary>
/// Best-effort detection of installed Claude clients to drive the connection wizard's per-client branching.
/// Detection is advisory only (the user can still configure either client); the testable cores are split
/// from the real-path glue so the logic is unit-testable.
/// </summary>
public static class ClaudeClientDetection
{
    private static readonly string[] CodeExeNames = { "claude.exe", "claude.cmd", "claude.bat", "claude" };

    public static ClaudeClients Detect() => new(DetectDesktop(), DetectCode());

    // ── Claude Desktop ────────────────────────────────────────────────────────

    /// <summary>Desktop is "installed" if a Store-Claude package container or the traditional %APPDATA%\Claude exists.</summary>
    public static bool DetectDesktop()
        => DetectDesktop(PackagePaths.RealLocalAppData(), PackagePaths.RealRoamingAppData(),
            ClaudePackagesUnder, Directory.Exists);

    /// <summary>Testable core — mirrors <see cref="ClaudeConfigLocator"/>'s resolution shape.</summary>
    public static bool DetectDesktop(
        string realLocalAppData, string realRoamingAppData,
        Func<string, IEnumerable<string>> enumerateClaudePackages, Func<string, bool> dirExists)
    {
        foreach (var packageDir in enumerateClaudePackages(Path.Combine(realLocalAppData, "Packages")))
            if (dirExists(Path.Combine(packageDir, "LocalCache", "Roaming", "Claude")))
                return true;
        return dirExists(Path.Combine(realRoamingAppData, "Claude"));
    }

    // ── Claude Code (CLI) ─────────────────────────────────────────────────────

    /// <summary>Code is "installed" if a <c>claude</c> launcher is on the PATH.</summary>
    public static bool DetectCode()
        => DetectCode(Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>(), File.Exists);

    /// <summary>Testable core — is a <c>claude</c> launcher present in any of the PATH directories?</summary>
    public static bool DetectCode(IEnumerable<string> pathDirectories, Func<string, bool> fileExists)
    {
        foreach (var dir in pathDirectories)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            foreach (var exe in CodeExeNames)
            {
                try { if (fileExists(Path.Combine(dir, exe))) return true; }
                catch { /* unreadable PATH entry */ }
            }
        }
        return false;
    }

    private static IEnumerable<string> ClaudePackagesUnder(string packagesRoot)
    {
        try { return Directory.GetDirectories(packagesRoot, "Claude_*"); }
        catch { return Array.Empty<string>(); }
    }
}
