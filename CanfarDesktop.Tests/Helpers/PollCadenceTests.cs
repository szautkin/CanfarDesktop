using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// How often the app asks CANFAR what changed — which, since every notification is a side effect of a
/// poll, is the same question as how late a notification arrives.
/// </summary>
public class PollCadenceTests
{
    /// <summary>
    /// A freshly built card must not open with a 45-second wait: the common case is that it was built
    /// because someone just arrived at the Portal to look at something.
    /// </summary>
    [Theory]
    [InlineData(PollCadence.JobsWatchSeconds)]
    [InlineData(PollCadence.SessionWatchSeconds)]
    public void TheFirstPollComesQuickly(int ceiling)
        => Assert.Equal(PollCadence.BusySeconds, new PollCadence(ceiling).Seconds);

    [Fact]
    public void AChangePutsItBackOnTheFastLane()
    {
        var cadence = new PollCadence(PollCadence.JobsWatchSeconds);
        cadence.Observe(inFlight: true, changed: false);
        cadence.Observe(inFlight: true, changed: false);
        Assert.True(cadence.Seconds > PollCadence.BusySeconds, "it never eased off");

        // A job just finished, or a new one appeared. Something else is likely about to happen, and
        // this is when delay is most visible.
        cadence.Observe(inFlight: true, changed: true);

        Assert.Equal(PollCadence.BusySeconds, cadence.Seconds);
    }

    /// <summary>
    /// A headless job can run for hours. Asking every five seconds for all of it would be thousands of
    /// requests to deliver one notification, on a shared platform, at a cost paid by everyone.
    /// </summary>
    [Theory]
    [InlineData(PollCadence.JobsWatchSeconds)]
    [InlineData(PollCadence.SessionWatchSeconds)]
    public void AQuietWatchEasesOffToItsCeiling(int ceiling)
    {
        var cadence = new PollCadence(ceiling);

        for (var i = 0; i < 20; i++)
        {
            cadence.Observe(inFlight: true, changed: false);
            Assert.True(cadence.Seconds <= ceiling, $"backoff overshot its ceiling to {cadence.Seconds}");
        }

        Assert.Equal(ceiling, cadence.Seconds);
    }

    /// <summary>
    /// The point of the whole change. Each surface's ceiling is at most the fixed interval it already
    /// ran at — 45s for the Batch Jobs card, 15s for the session strip — so every interval reachable
    /// from any history of polls is one that surface would have used anyway.
    /// </summary>
    [Theory]
    [InlineData(PollCadence.JobsWatchSeconds, 45)]
    [InlineData(PollCadence.SessionWatchSeconds, 15)]
    public void NoNotificationCanArriveLaterThanItUsedTo(int ceiling, int wasFixedAt)
    {
        var cadence = new PollCadence(ceiling);

        for (var step = 0; step < 50; step++)
        {
            // Alternate, so both the eased and the quickened branch are exercised from every reachable
            // state rather than only from the first one.
            cadence.Observe(inFlight: true, changed: step % 7 == 0);

            Assert.True(cadence.Seconds <= wasFixedAt,
                $"a notification can now be {cadence.Seconds}s late, up from {wasFixedAt}s");
        }
    }

    /// <summary>
    /// No pending session and no unfinished job: nothing to report, so the poll buys nothing and should
    /// cost what the slowest surface always cost. Anything faster here is a sustained load increase for
    /// no benefit — on a Portal left open all day.
    /// </summary>
    [Fact]
    public void NothingInFlightCostsNoMoreThanItUsedTo()
    {
        var cadence = new PollCadence(PollCadence.JobsWatchSeconds);
        cadence.Observe(inFlight: false, changed: false);

        Assert.Equal(PollCadence.IdleSeconds, cadence.Seconds);
    }

    /// <summary>Coming back from idle is a change, so it is not a slow crawl back up.</summary>
    [Fact]
    public void WorkAppearingAfterAQuietSpellIsPickedUpAtOnce()
    {
        var cadence = new PollCadence(PollCadence.JobsWatchSeconds);
        cadence.Observe(inFlight: false, changed: false);

        cadence.Observe(inFlight: true, changed: true);

        Assert.Equal(PollCadence.BusySeconds, cadence.Seconds);
    }

    /// <summary>
    /// A ceiling under the busy interval — not used today, but the arithmetic must not hand back an
    /// interval faster than the surface asked to allow.
    /// </summary>
    [Fact]
    public void ACeilingBelowTheBusyIntervalIsStillTheCeiling()
    {
        var cadence = new PollCadence(3);
        Assert.Equal(3, cadence.Seconds);

        cadence.Observe(inFlight: true, changed: true);
        Assert.Equal(3, cadence.Seconds);
    }
}
