namespace CanfarDesktop.Mcp.Config;

/// <summary>
/// Locates and (on explicit user confirmation) repairs the Claude Desktop config file, delegating the
/// actual merge to the pure <see cref="ClaudeConfigMerge"/>. Writes atomically (temp + replace) and
/// keeps a <c>.bak</c> of any prior file. The config path is injectable so the path/preview/write logic
/// is unit-testable without touching the real %APPDATA% location.
/// </summary>
public sealed class ClaudeConfigRepair
{
    public string ConfigPath { get; }

    public ClaudeConfigRepair(string? configPath = null)
        => ConfigPath = configPath ?? DefaultConfigPath();

    /// <summary>The Claude Desktop config location: the real <c>%APPDATA%\Claude\claude_desktop_config.json</c> (un-redirected — see <see cref="Helpers.PackagePaths"/>).</summary>
    public static string DefaultConfigPath()
        => Path.Combine(
            Helpers.PackagePaths.RealRoamingAppData(),
            "Claude", "claude_desktop_config.json");

    /// <summary>The current config text, or null when absent/unreadable.</summary>
    public string? ReadExisting()
    {
        try { return File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath) : null; }
        catch { return null; }
    }

    /// <summary>
    /// Whether the config already references the Verbinal bridge. The single owner of this heuristic —
    /// the wizard's resume logic and the diagnostics panel must agree, and this class knows what
    /// <see cref="Apply"/> writes.
    /// </summary>
    public bool IsBridgeRegistered() => ContainsBridgeReference(ReadExisting());

    /// <summary>The raw heuristic over config text (for callers that read the config themselves).</summary>
    public static bool ContainsBridgeReference(string? configText)
        => configText?.Contains("McpBridge", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>The merged config that <see cref="Apply"/> would write — for a confirmation preview.</summary>
    public string Preview(string command) => ClaudeConfigMerge.MergedRoot(ReadExisting(), command);

    /// <summary>
    /// True when a direct write can't work and the user must edit the file themselves: MSIX write
    /// virtualization would CoW-sandbox this process's write (it would look successful to us but be
    /// invisible to Claude Desktop). Store-container Claude configs live under the exempt
    /// <c>Packages\</c> tree and are writable; a traditional install's real <c>%APPDATA%\Claude\</c>
    /// is not. Must be decided BEFORE writing — a read-back check would falsely pass (merged view).
    /// </summary>
    public bool RequiresManualEdit => Helpers.PackagePaths.IsWriteVirtualized(ConfigPath);

    /// <summary>Atomically write the merged config, backing up any existing file to <c>.bak</c> first.</summary>
    public void Apply(string command)
    {
        if (RequiresManualEdit)
            throw new InvalidOperationException(
                $"A write to '{ConfigPath}' would be sandboxed by MSIX virtualization and invisible " +
                "to Claude Desktop. Edit the file manually (callers should check RequiresManualEdit " +
                "and offer the merged JSON instead).");

        var merged = Preview(command);
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);

        if (File.Exists(ConfigPath))
            File.Copy(ConfigPath, ConfigPath + ".bak", overwrite: true);

        var tmp = ConfigPath + ".tmp";
        File.WriteAllText(tmp, merged);
        if (File.Exists(ConfigPath)) File.Replace(tmp, ConfigPath, null);
        else File.Move(tmp, ConfigPath);
    }
}
