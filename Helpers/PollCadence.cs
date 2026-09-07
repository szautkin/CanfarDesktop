namespace CanfarDesktop.Helpers;

/// <summary>
/// How often the app asks CANFAR what changed.
///
/// The session strip and the Batch Jobs card both learn about events by polling, and every notification
/// they raise is a side effect of a poll. So the poll interval IS the notification delay: a session that
/// came up, or a job that failed, was announced up to 15 or 45 seconds after it happened — long enough
/// that the notification reads as being about something else. Worse, a job that started and finished
/// inside one 45-second window was never seen in a non-terminal state at all, so no transition was
/// detected and nothing was ever announced.
///
/// Polling flat out is not the answer either. A headless job can run for hours, and a fixed five-second
/// interval would ask about it some two thousand times to deliver one notification — on a shared
/// platform, at a cost paid by everyone.
///
/// So the cadence follows the evidence. Something just changed? The next change is probably close: ask
/// again soon. Poll after poll of nothing? Ease off, doubling each time, up to a ceiling. Nothing in
/// flight at all — no pending session, no unfinished job? There is no notification to be timely about,
/// so drop to <see cref="IdleSeconds"/> and stop spending requests on it.
///
/// The ceiling is per-surface and is at most the interval that surface already ran at, which makes the
/// change one-directional: no notification arrives later than it would have before, and the ones near a
/// real event arrive much sooner.
/// </summary>
public struct PollCadence
{
    /// <summary>
    /// The quickest the app will ask, and so the shortest a notification delay can be. Used for the
    /// poll right after something changed.
    /// </summary>
    public const int BusySeconds = 5;

    /// <summary>
    /// Ceiling for the Batch Jobs card while a job of the user's is still running.
    ///
    /// Below the 45 seconds the card used to run at unconditionally, which is the interval that made a
    /// finished job's notification read as being about something else. A job can run for hours, so this
    /// is also the steady-state cost of watching one: one list call every twenty seconds, and only while
    /// there is something to watch.
    /// </summary>
    public const int JobsWatchSeconds = 20;

    /// <summary>
    /// Ceiling for the session strip while a session is pending.
    ///
    /// Well under the jobs ceiling, and under the flat 15s the strip used to run at, because this loop
    /// only runs while something is pending — a bounded window, usually a minute or two, at the end of
    /// which sits the single most-awaited notification in the app. Someone is watching the screen for
    /// it, so the extra handful of list calls buys the thing worth buying.
    /// </summary>
    public const int SessionWatchSeconds = 8;

    /// <summary>
    /// The interval when nothing is in flight at all.
    ///
    /// No pending session and no unfinished job means no transition to report, so there is nothing to be
    /// timely about and a frequent poll is pure load. It still happens, because a session can be
    /// launched from another machine.
    /// </summary>
    public const int IdleSeconds = 45;

    private readonly int _watchCeiling;

    /// <summary>
    /// A cadence that eases off to <paramref name="watchCeiling"/> while work is in flight.
    ///
    /// Starts quick: the first poll is what discovers whether anything is in flight, and starting slow
    /// would mean starting slow on exactly the case that needs to be fast — the one where the user has
    /// just launched something and is watching for it.
    /// </summary>
    public PollCadence(int watchCeiling)
    {
        _watchCeiling = watchCeiling;
        Seconds = Math.Min(BusySeconds, watchCeiling);
    }

    /// <summary>Seconds to wait before the next poll.</summary>
    public int Seconds { get; private set; }

    /// <summary>
    /// Fold in what the poll just saw.
    /// </summary>
    /// <param name="inFlight">
    /// Can anything still change state — is a notification even possible?
    /// </param>
    /// <param name="changed">Did anything actually move since last time?</param>
    public void Observe(bool inFlight, bool changed)
        => Seconds = !inFlight ? IdleSeconds
            : changed ? Math.Min(BusySeconds, _watchCeiling)
            : Math.Min(Seconds * 2, _watchCeiling);
}
