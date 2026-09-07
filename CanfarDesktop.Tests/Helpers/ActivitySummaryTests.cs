using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// The words on the status bar.
///
/// A status bar becomes furniture the moment it stops being accurate — "Idle" while something is
/// running, a permanent "0 failed", a row that says "Discovering…" for every one of three jobs. These
/// pin the wording so it keeps being worth a strip of chrome.
/// </summary>
public class ActivitySummaryTests
{
    private static TrackedTask Task(
        string label,
        TaskProgress progress = TaskProgress.Running,
        string stage = "",
        string? message = null,
        TimeSpan? elapsed = null)
    {
        var started = DateTimeOffset.UtcNow - (elapsed ?? TimeSpan.FromSeconds(3));
        return new TrackedTask(1, TaskKind.Discovery, label, stage, progress, message,
            started, progress == TaskProgress.Running ? null : DateTimeOffset.UtcNow);
    }

    // ── The one line ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NothingRunningIsIdle()
        => Assert.Equal("Idle", ActivitySummary.Line([]));

    [Fact]
    public void FinishedWorkIsStillIdle()
        => Assert.Equal("Idle", ActivitySummary.Line([Task("probe", TaskProgress.Succeeded)]));

    /// <summary>
    /// One task names itself, WITH its stage — which is the whole reason the registry keeps stages. A
    /// bare "1 task running" is the boolean this replaced.
    /// </summary>
    [Fact]
    public void OneRunningTaskSaysWhichAndHowFar()
        => Assert.Equal("Inspect skaha/base:1.0 — waiting for job vi-abc",
            ActivitySummary.Line([Task("Inspect skaha/base:1.0", stage: "waiting for job vi-abc")]));

    [Fact]
    public void OneRunningTaskWithNoStageYetIsJustItsName()
        => Assert.Equal("Inspect skaha/base:1.0", ActivitySummary.Line([Task("Inspect skaha/base:1.0")]));

    /// <summary>Five labels on one line is not a line anybody reads.</summary>
    [Fact]
    public void SeveralRunningTasksBecomeACount()
        => Assert.Equal("3 tasks running",
            ActivitySummary.Line([Task("a"), Task("b"), Task("c")]));

    [Fact]
    public void FinishedWorkDoesNotCountTowardsTheRunningTotal()
        => Assert.Equal("a", ActivitySummary.Line([Task("a"), Task("b", TaskProgress.Succeeded)]));

    // ── The failure count ───────────────────────────────────────────────────────────────────────

    /// <summary>A permanent zero is noise; one that appears is information.</summary>
    [Fact]
    public void NoFailuresSaysNothingRatherThanZero()
        => Assert.Null(ActivitySummary.Failures([Task("a"), Task("b", TaskProgress.Succeeded)]));

    [Fact]
    public void FailuresAreCounted()
        => Assert.Equal("2 failed", ActivitySummary.Failures(
            [Task("a", TaskProgress.Failed, message: "no"), Task("b", TaskProgress.Failed, message: "no")]));

    /// <summary>Abandoned work went wrong too — it is exactly the case that used to look like nothing.</summary>
    [Fact]
    public void AbandonedWorkCountsAsFailed()
        => Assert.Equal("1 failed", ActivitySummary.Failures([Task("a", TaskProgress.Cancelled)]));

    // ── The expanded list ───────────────────────────────────────────────────────────────────────

    /// <summary>A list you opened to find out what just happened should start with what just happened.</summary>
    [Fact]
    public void TheListReadsNewestFirst()
    {
        var lines = ActivitySummary.Lines([Task("first"), Task("second"), Task("third")]);

        Assert.Equal(["third", "second", "first"], lines.Select(l => l.Title));
    }

    /// <summary>A failure with no reason is the thing the registry exists to stop; the row shows it.</summary>
    [Fact]
    public void AFailedRowShowsItsReason()
        => Assert.Equal("the service refused it: 413",
            ActivitySummary.Describe(Task("Upload", TaskProgress.Failed, message: "the service refused it: 413")).Detail);

    [Fact]
    public void AFailedRowWithNoReasonStillSaysItFailed()
        => Assert.Equal("failed", ActivitySummary.Describe(Task("Upload", TaskProgress.Failed)).Detail);

    /// <summary>Nobody chose this, so "cancelled" would be a lie about what happened.</summary>
    [Fact]
    public void AnAbandonedRowSaysNobodyFinishedIt()
        => Assert.Equal("abandoned before it finished",
            ActivitySummary.Describe(Task("Upload", TaskProgress.Cancelled)).Detail);

    [Fact]
    public void ARunningRowShowsItsStageAndHowLongItHasBeenThere()
        => Assert.Equal("waiting for job vi-abc · 30s",
            ActivitySummary.Describe(
                Task("Inspect", stage: "waiting for job vi-abc", elapsed: TimeSpan.FromSeconds(30))).Detail);

    [Fact]
    public void ASucceededRowShowsHowLongItTook()
        => Assert.Equal("5s",
            ActivitySummary.Describe(
                Task("Inspect", TaskProgress.Succeeded, elapsed: TimeSpan.FromSeconds(5))).Detail);

    [Fact]
    public void TheRowKeepsTheProgressSoTheListCanColourIt()
        => Assert.Equal(TaskProgress.Failed,
            ActivitySummary.Describe(Task("Upload", TaskProgress.Failed, message: "no")).Progress);

    // ── Durations ───────────────────────────────────────────────────────────────────────────────

    /// <summary>A number that changes eight times a second is not readable, and is not worth reading.</summary>
    [Fact]
    public void SubSecondWorkIsJustNow()
        => Assert.Equal("just now", ActivitySummary.Duration(TimeSpan.FromMilliseconds(420)));

    [Theory]
    [InlineData(1, "1s")]
    [InlineData(59, "59s")]
    public void SecondsUpToAMinute(int seconds, string expected)
        => Assert.Equal(expected, ActivitySummary.Duration(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void MinutesCarryTheirSeconds()
        => Assert.Equal("3m 7s", ActivitySummary.Duration(TimeSpan.FromSeconds(187)));

    [Fact]
    public void HoursCarryTheirMinutes()
        => Assert.Equal("2h 5m", ActivitySummary.Duration(TimeSpan.FromMinutes(125)));

    // ── Translation ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The hook exists because the resource loader needs a packaged app and this has to be testable
    /// without one. Unset, it must answer in English rather than in resource keys.
    /// </summary>
    [Fact]
    public void TranslationsGoThroughTheHookAndFallBackToEnglish()
    {
        var previous = ActivitySummary.Translate;
        try
        {
            ActivitySummary.Translate = key => key == "Activity_Idle" ? "Au repos" : null;

            Assert.Equal("Au repos", ActivitySummary.Line([]));
            Assert.Equal("2 tasks running", ActivitySummary.Line([Task("a"), Task("b")]));   // no French, English stands
        }
        finally
        {
            ActivitySummary.Translate = previous;
        }
    }
}
