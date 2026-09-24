using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// Where remote compute stands, and what the Remote Compute screen offers in each state.
///
/// <para>The screen's buttons are only as right as these rules: Start on a session that is already
/// starting launches nothing but confuses, and Stop hidden on a failed session leaves its name taken
/// and its cores held with no way to let them go.</para>
/// </summary>
public class ComputeStatusTests
{
    [Fact]
    public void WithNoImageItIsNotSetUpWhateverThePlatformSays()
        => Assert.Equal(ComputeState.NotSetUp, ComputeStatus.From(configured: false, "Running"));

    [Theory]
    [InlineData(null, ComputeState.Stopped)]
    [InlineData("", ComputeState.Stopped)]
    [InlineData("Pending", ComputeState.Starting)]
    [InlineData("Running", ComputeState.Running)]
    [InlineData("running", ComputeState.Running)]
    [InlineData("Terminating", ComputeState.Stopping)]
    [InlineData("Failed", ComputeState.Failed)]
    [InlineData("Error", ComputeState.Failed)]
    [InlineData("Succeeded", ComputeState.Stopped)]
    [InlineData("Completed", ComputeState.Stopped)]
    public void ASessionStatusMeansAState(string? status, ComputeState expected)
        => Assert.Equal(expected, ComputeStatus.From(configured: true, status));

    [Theory]
    [InlineData(ComputeState.NotSetUp, false, false, false)]
    [InlineData(ComputeState.Stopped, true, false, true)]
    [InlineData(ComputeState.Starting, false, true, true)]
    [InlineData(ComputeState.Running, false, true, true)]
    [InlineData(ComputeState.Stopping, false, false, false)]
    [InlineData(ComputeState.Failed, true, true, true)]
    public void EachStateOffersWhatMakesSenseInIt(ComputeState state, bool start, bool stop, bool run)
    {
        Assert.Equal(start, ComputeStatus.CanStart(state));
        Assert.Equal(stop, ComputeStatus.CanStop(state));
        Assert.Equal(run, ComputeStatus.CanRun(state));
    }

    [Fact]
    public void UptimeIsHowLongSinceTheSessionStarted()
    {
        var now = new DateTimeOffset(2026, 9, 24, 12, 30, 0, TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromMinutes(90), ComputeStatus.Uptime("2026-09-24T11:00:00Z", now));
    }

    /// <summary>The platform's times carry no zone; they are UTC, not the machine's local time.</summary>
    [Fact]
    public void AStartTimeWithoutAZoneIsUtc()
    {
        var now = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromMinutes(15), ComputeStatus.Uptime("2026-09-24T11:45:00", now));
    }

    /// <summary>A clock that disagrees with the platform's says nothing, rather than a negative uptime.</summary>
    [Theory]
    [InlineData("2026-09-24T13:00:00Z")]
    [InlineData("not a time")]
    [InlineData(null)]
    public void AnUptimeThatCannotBeTrueIsNone(string? started)
        => Assert.Null(ComputeStatus.Uptime(started, new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero)));
}
