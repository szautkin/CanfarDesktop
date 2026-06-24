using Windows.Storage;

namespace CanfarDesktop.Mcp;

/// <summary>
/// Persists the single MCP knob — whether the in-app named-pipe MCP server runs. Opt-in: defaults to
/// OFF so the app never opens a server surface unless the user explicitly enables it. Stored in
/// LocalSettings (same pattern as <c>ImageDiscoverySettingsService</c>); tolerant of the unpackaged case.
/// </summary>
public sealed class McpSettingsService
{
    private const string KeyEnabled = "Mcp.Enabled";
    private const string KeyAutoApply = "Mcp.AutoApplyWrites";
    private const string KeyFollowActivity = "Mcp.FollowAgentActivity";

    private readonly ApplicationDataContainer? _localSettings;

    public McpSettingsService()
    {
        try { _localSettings = ApplicationData.Current.LocalSettings; }
        catch { _localSettings = null; }
    }

    /// <summary>Whether the MCP server should run. Defaults to false (the user must opt in to the server).</summary>
    public bool Enabled
    {
        get => ReadBool(KeyEnabled, defaultValue: false);
        set => WriteBool(KeyEnabled, value);
    }

    /// <summary>Whether agent write proposals auto-apply (vs. queue for review). Defaults to true (1-to-1 with macOS).</summary>
    public bool AutoApplyEnabled
    {
        get => ReadBool(KeyAutoApply, defaultValue: true);
        set => WriteBool(KeyAutoApply, value);
    }

    /// <summary>Whether the app navigates the user to the relevant view after an applied write. Defaults to true.</summary>
    public bool FollowAgentActivityEnabled
    {
        get => ReadBool(KeyFollowActivity, defaultValue: true);
        set => WriteBool(KeyFollowActivity, value);
    }

    private bool ReadBool(string key, bool defaultValue)
        => _localSettings?.Values.TryGetValue(key, out var v) == true && v is bool b ? b : defaultValue;

    private void WriteBool(string key, bool value)
    {
        if (_localSettings is not null) _localSettings.Values[key] = value;
    }
}
