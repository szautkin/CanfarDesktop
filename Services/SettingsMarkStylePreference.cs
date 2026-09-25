using CanfarDesktop.Helpers;
using CanfarDesktop.Models;

namespace CanfarDesktop.Services;

/// <summary>
/// The stored preference, as one settings value.
///
/// A default, not the storage: every mark carries its own style, because it persists, travels over MCP
/// and ends up in an exported figure that has to look the same when reopened.
/// </summary>
public sealed class SettingsMarkStylePreference : IMarkStylePreference
{
    private readonly Func<ISettingsService?> _settings;

    public SettingsMarkStylePreference(Func<ISettingsService?> settings) => _settings = settings;

    public MarkStyle Default
    {
        get
        {
            try { return MarkStyle.Decode(_settings()?.DefaultMarkStyle, MarkStyle.UserDefault); }
            catch { return MarkStyle.UserDefault; }
        }
        set
        {
            try
            {
                if (_settings() is not { } settings) return;
                settings.DefaultMarkStyle = value.Encode();
                settings.Save();
            }
            catch
            {
                // A preference that could not be stored is not worth interrupting drawing for.
            }
        }
    }
}
