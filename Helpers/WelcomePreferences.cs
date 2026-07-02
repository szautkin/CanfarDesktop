using Windows.Storage;

namespace CanfarDesktop.Helpers;

/// <summary>
/// One-shot bookkeeping for the first-run Welcome dialog (macOS <c>WelcomePreferences</c> parity):
/// store the Welcome version the user last saw; bump <see cref="CurrentVersion"/> to re-surface
/// the dialog for everyone (e.g. after a major feature wave).
/// </summary>
public static class WelcomePreferences
{
    /// <summary>Bump to re-show the Welcome dialog to everyone on the next launch.</summary>
    public const int CurrentVersion = 1;

    private const string SeenVersionKey = "verbinal.welcome.seenVersion";

    public static int SeenVersion
    {
        get
        {
            try
            {
                return ApplicationData.Current.LocalSettings.Values.TryGetValue(SeenVersionKey, out var v) && v is int i
                    ? i
                    : 0;
            }
            // Unpackaged run — nothing can persist the stamp, so suppress the dialog
            // rather than re-welcoming on every launch.
            catch { return int.MaxValue; }
        }
    }

    public static void MarkSeen()
    {
        try { ApplicationData.Current.LocalSettings.Values[SeenVersionKey] = CurrentVersion; }
        catch { /* unpackaged — best-effort */ }
    }
}
