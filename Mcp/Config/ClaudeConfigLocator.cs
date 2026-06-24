using CanfarDesktop.Helpers;

namespace CanfarDesktop.Mcp.Config;

/// <summary>
/// Finds the <c>claude_desktop_config.json</c> that Claude Desktop actually reads. The Store/MSIX build
/// of Claude reads from its own package container
/// (<c>%LOCALAPPDATA%\Packages\Claude_*\LocalCache\Roaming\Claude\</c>); a traditional install reads the
/// real <c>%APPDATA%\Claude\</c>. We must NOT use our own (redirected) AppData — see <see cref="PackagePaths"/>.
/// </summary>
public static class ClaudeConfigLocator
{
    public const string ConfigFileName = "claude_desktop_config.json";

    /// <summary>Resolve using the real machine paths.</summary>
    public static string Resolve()
        => Resolve(PackagePaths.RealLocalAppData(), PackagePaths.RealRoamingAppData(), EnumerateClaudePackages, Directory.Exists);

    /// <summary>Testable core: prefer a Store-Claude container that already has a Claude config dir, else the real %APPDATA%.</summary>
    public static string Resolve(
        string realLocalAppData,
        string realRoamingAppData,
        Func<string, IEnumerable<string>> enumerateClaudePackages,
        Func<string, bool> dirExists)
    {
        foreach (var packageDir in enumerateClaudePackages(Path.Combine(realLocalAppData, "Packages")))
        {
            var claudeDir = Path.Combine(packageDir, "LocalCache", "Roaming", "Claude");
            if (dirExists(claudeDir))
                return Path.Combine(claudeDir, ConfigFileName);
        }

        return Path.Combine(realRoamingAppData, "Claude", ConfigFileName);
    }

    private static IEnumerable<string> EnumerateClaudePackages(string packagesRoot)
    {
        try { return Directory.GetDirectories(packagesRoot, "Claude_*"); }
        catch { return Array.Empty<string>(); }
    }
}
