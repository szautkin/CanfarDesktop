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

    private readonly ApplicationDataContainer? _localSettings;

    public McpSettingsService()
    {
        try { _localSettings = ApplicationData.Current.LocalSettings; }
        catch { _localSettings = null; }
    }

    /// <summary>Whether the MCP server should run. Defaults to false (the user must opt in).</summary>
    public bool Enabled
    {
        get => _localSettings?.Values.TryGetValue(KeyEnabled, out var v) == true && v is bool b && b;
        set { if (_localSettings is not null) _localSettings.Values[KeyEnabled] = value; }
    }
}
