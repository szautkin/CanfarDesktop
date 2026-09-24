namespace CanfarDesktop.Helpers;

/// <summary>
/// The screens that are the person's CANFAR account at work — Portal, Remote Compute and Storage — and
/// so stay locked until they sign in: dimmed with a lock on the landing page, and behind the sign-in
/// dialog however they are opened, whether from a tile, by navigate_to or from a workflow step.
///
/// <para>Keyed by the names navigate_to takes. The one list the landing tiles, navigation and
/// follow-agent-activity all read, so a screen cannot be locked in one of them and open in another.</para>
/// </summary>
public static class AccountScreens
{
    public static IReadOnlySet<string> All { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "portal", "remoteCompute", "storage" };

    /// <summary>Whether <paramref name="screen"/> needs the person signed in.</summary>
    public static bool Contains(string? screen) => screen is not null && All.Contains(screen);
}
