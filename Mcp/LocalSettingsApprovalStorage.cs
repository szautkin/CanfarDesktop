using System.Text.Json;
using Windows.Storage;

namespace CanfarDesktop.Mcp;

/// <summary>
/// LocalSettings-backed <see cref="IMcpApprovalStorage"/> (ApplicationData); the require-approval flag is a
/// bool, the allow-list a JSON string array. Tolerant of the unpackaged case (null container), like
/// <see cref="McpSettingsService"/>.
/// </summary>
public sealed class LocalSettingsApprovalStorage : IMcpApprovalStorage
{
    private const string KeyRequireApproval = "Mcp.RequireClientApproval";
    private const string KeyApprovedClients = "Mcp.ApprovedClients"; // JSON string array

    private readonly ApplicationDataContainer? _settings;

    public LocalSettingsApprovalStorage()
    {
        try { _settings = ApplicationData.Current.LocalSettings; }
        catch { _settings = null; }
    }

    public bool RequireApproval
    {
        get => _settings?.Values.TryGetValue(KeyRequireApproval, out var v) == true && v is bool b && b;
        set { if (_settings is not null) _settings.Values[KeyRequireApproval] = value; }
    }

    public HashSet<string> LoadApproved()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (_settings?.Values.TryGetValue(KeyApprovedClients, out var v) == true && v is string json && json.Length > 0)
        {
            try
            {
                foreach (var id in JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>())
                    if (!string.IsNullOrEmpty(id)) set.Add(id);
            }
            catch (JsonException) { /* corrupt value — start empty */ }
        }
        return set;
    }

    public void SaveApproved(IReadOnlyCollection<string> approved)
    {
        if (_settings is not null) _settings.Values[KeyApprovedClients] = JsonSerializer.Serialize(approved);
    }
}
