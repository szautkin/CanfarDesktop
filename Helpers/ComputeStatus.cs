using System.Globalization;

namespace CanfarDesktop.Helpers;

/// <summary>Where the remote compute stands, as the Remote Compute screen and its tile show it.</summary>
public enum ComputeState
{
    /// <summary>No compute image in Settings: run_code cannot run at all.</summary>
    NotSetUp,

    /// <summary>Set up, with no compute session on the platform.</summary>
    Stopped,

    /// <summary>The session has been asked for and is not running yet — a contributed session takes a minute or two.</summary>
    Starting,

    Running,

    /// <summary>The session is being torn down.</summary>
    Stopping,

    /// <summary>The platform says the session failed; it still holds its name until it is deleted.</summary>
    Failed,
}

/// <summary>
/// The rules behind the Remote Compute screen's status, kept apart from the screen so they can be
/// tested without a platform: which state a session's status means, what can be done in each, and how
/// long the session has been up.
/// </summary>
public static class ComputeStatus
{
    /// <summary>
    /// The state for a compute session's platform status. Null or empty means there is no session:
    /// stopped where an image is set, not set up where none is. A finished session (Succeeded,
    /// Completed) counts as none — it holds nothing and runs nothing.
    ///
    /// <para>A live session is what it is either way. It used to read "not set up" wherever no image
    /// was set, so after a reinstall a session still running from before could be neither seen nor
    /// stopped from the Remote Compute screen.</para>
    /// </summary>
    public static ComputeState From(bool configured, string? sessionStatus)
    {
        var none = configured ? ComputeState.Stopped : ComputeState.NotSetUp;
        if (string.IsNullOrWhiteSpace(sessionStatus)) return none;

        return sessionStatus.Trim().ToLowerInvariant() switch
        {
            "pending" => ComputeState.Starting,
            "running" => ComputeState.Running,
            "terminating" => ComputeState.Stopping,
            "failed" or "error" => ComputeState.Failed,
            _ => none,
        };
    }

    /// <summary>A state as an agent reads it: <c>notSetUp</c>, <c>running</c> — the enum's name in camelCase.</summary>
    public static string Name(ComputeState state)
    {
        var name = state.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    /// <summary>
    /// Start is offered when there is no session to reuse — a failed one is replaced — and an image is
    /// set: launching needs the one the person chose, and choosing it is their consent.
    /// </summary>
    public static bool CanStart(ComputeState state, bool configured)
        => configured && state is ComputeState.Stopped or ComputeState.Failed;

    /// <summary>
    /// Stop is offered whenever a session exists and is not already going away — set up or not: a
    /// session nobody can stop is the thing to avoid.
    /// </summary>
    public static bool CanStop(ComputeState state)
        => state is ComputeState.Starting or ComputeState.Running or ComputeState.Failed;

    /// <summary>Code can be sent whenever the compute is set up: a stopped session is started for it.</summary>
    public static bool CanRun(ComputeState state, bool configured)
        => configured && state is not ComputeState.NotSetUp and not ComputeState.Stopping;

    /// <summary>
    /// How long the session has been up, from the platform's start time. Null when the time does not
    /// parse or lies in the future — a clock that disagrees with the platform's should say nothing
    /// rather than a negative uptime.
    /// </summary>
    public static TimeSpan? Uptime(string? startedAt, DateTimeOffset now)
    {
        if (!DateTimeOffset.TryParse(startedAt, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var started))
            return null;

        var up = now - started;
        return up < TimeSpan.Zero ? null : up;
    }
}
