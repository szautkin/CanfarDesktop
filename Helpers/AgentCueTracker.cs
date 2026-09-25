namespace CanfarDesktop.Helpers;

/// <summary>
/// When to sound a cue: on the EDGES of an agent working, not on every tool call.
///
/// The activity signal is raised once per tool call, and an agent doing real work raises it many times a
/// second. Sounding each one would be a stutter rather than a cue, so this collapses a burst of signals
/// into one "started" and — when the idle timer finally runs out — one "finished".
///
/// Split out from the window because the edge logic is the part worth being sure of, and a window is the
/// one thing a test cannot make.
/// </summary>
public sealed class AgentCueTracker
{
    /// <summary>Whether an agent is currently considered to be working.</summary>
    public bool Working { get; private set; }

    /// <summary>A tool call happened. Returns the cue to play, or null when already working.</summary>
    public AgentCue? Activity()
    {
        if (Working) return null;
        Working = true;
        return AgentCue.Started;
    }

    /// <summary>The idle timer ran out. Returns the cue to play, or null when it wasn't working anyway.</summary>
    public AgentCue? Idle()
    {
        if (!Working) return null;
        Working = false;
        return AgentCue.Finished;
    }
}
