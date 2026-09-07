using Xunit;
using CanfarDesktop.Models;
using CanfarDesktop.Services;

namespace CanfarDesktop.Tests.Services;

/// <summary>
/// What the app remembers about jobs CANFAR no longer has.
///
/// Skaha reaps headless jobs, and the discovery coordinator deletes its own probes the moment they
/// finish. So a job could fail and, a minute later, the app had nothing to say about it: gone from the
/// listing, logs and events gone with it, and the only trace a count that had ticked from Running to
/// Failed.
/// </summary>
public class JobHistoryStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "job-history-" + Guid.NewGuid().ToString("N"));

    private JobHistoryStore Store() => new(Path.Combine(_dir, "job_history.json"));

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* a temp directory that outlives one test run is not a failure */ }
    }

    private static JobRecord Job(
        string id,
        JobOutcome outcome = JobOutcome.Failed,
        string? reason = null,
        JobOrigin origin = JobOrigin.User) => new()
    {
        Id = id,
        Name = $"job-{id}",
        Image = "images.canfar.net/skaha/base:1.0",
        Origin = origin,
        Outcome = outcome,
        Status = outcome == JobOutcome.Failed ? "Failed" : "Succeeded",
        FinishedAt = "2026-09-05T10:00:00Z",
        FailureReason = reason,
    };

    // ── Remembering ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The whole point. A status of "Failed" is not a reason, and by the time anyone reads it the job
    /// and its logs are gone.
    /// </summary>
    [Fact]
    public void AFailedJobKeepsItsReasonAcrossRestarts()
    {
        Store().Record(Job("a", reason: "OOMKilled: container exceeded 8G"));

        var only = Assert.Single(Store().All());   // a second store, i.e. a new launch
        Assert.Equal("OOMKilled: container exceeded 8G", only.FailureReason);
        Assert.Equal(JobOutcome.Failed, only.Outcome);
    }

    [Fact]
    public void NothingRememberedIsAnEmptyHistoryRatherThanAFailure()
        => Assert.Empty(Store().All());

    /// <summary>What just happened is what somebody opening this is looking for.</summary>
    [Fact]
    public void TheNewestIsFirst()
    {
        var store = Store();
        store.Record(Job("first"));
        store.Record(Job("second"));

        Assert.Equal(["second", "first"], store.All().Select(j => j.Id));
    }

    /// <summary>
    /// The poller can notice a job failed before the reason has been fetched, so the record that
    /// carries the reason has to win rather than be skipped as a duplicate.
    /// </summary>
    [Fact]
    public void RecordingTheSameJobAgainReplacesItRatherThanDuplicatingIt()
    {
        var store = Store();
        store.Record(Job("a", reason: null));
        store.Record(Job("a", reason: "the node was drained"));

        var only = Assert.Single(store.All());
        Assert.Equal("the node was drained", only.FailureReason);
    }

    [Fact]
    public void AJobWithNoIdIsNotRemembered()
    {
        var store = Store();
        store.Record(Job(string.Empty));

        Assert.Empty(store.All());
    }

    /// <summary>Written when it was recorded, if the caller did not say — a record with no time is not one.</summary>
    [Fact]
    public void AJobWithNoFinishTimeGetsOne()
    {
        var store = Store();
        store.Record(Job("a") with { FinishedAt = string.Empty });

        Assert.False(string.IsNullOrEmpty(Assert.Single(store.All()).FinishedAt));
    }

    // ── Staying bounded ─────────────────────────────────────────────────────────────────────────

    /// <summary>A morning of failed probes must not push the interesting one off the end too soon.</summary>
    [Fact]
    public void TheHistoryIsBounded()
    {
        var store = Store();
        for (var i = 0; i < JobHistoryStore.MaxJobs * 2; i++)
            store.Record(Job($"job-{i}"));

        Assert.Equal(JobHistoryStore.MaxJobs, store.All().Count);
    }

    [Fact]
    public void TheOldestAreTheOnesForgotten()
    {
        var store = Store();
        for (var i = 0; i < JobHistoryStore.MaxJobs + 5; i++)
            store.Record(Job($"job-{i}"));

        Assert.DoesNotContain(store.All(), j => j.Id == "job-0");
        Assert.Contains(store.All(), j => j.Id == $"job-{JobHistoryStore.MaxJobs + 4}");
    }

    [Fact]
    public void ClearingForgetsEverything()
    {
        var store = Store();
        store.Record(Job("a"));

        store.Clear();

        Assert.Empty(Store().All());
    }

    // ── Telling the card ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ChangedIsRaisedOnEveryWrite()
    {
        var store = Store();
        var raised = 0;
        store.Changed += () => raised++;

        store.Record(Job("a"));
        store.Clear();

        Assert.Equal(2, raised);
    }

    // ── The row ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>A probe the app ran on someone's behalf is not a job they submitted, and reads differently.</summary>
    [Fact]
    public void AProbeSaysWhatItWasInspecting()
    {
        var probe = Job("a", origin: JobOrigin.ImageProbe) with { TargetImage = "skaha/astroml:24.10" };

        Assert.Equal("Image inspection — skaha/astroml:24.10", probe.Summary);
    }

    [Fact]
    public void AUserJobIsNamedAsOne()
        => Assert.Equal("Batch job", Job("a").Summary);

    /// <summary>
    /// The enum names, not their ordinals. A file written by one build and read by the next must not
    /// turn every failure into a success because a member was inserted above it.
    /// </summary>
    [Fact]
    public void OutcomesAreStoredByNameSoTheFileSurvivesAnEnumChange()
    {
        Store().Record(Job("a", JobOutcome.Failed, reason: "no"));

        var json = File.ReadAllText(Path.Combine(_dir, "job_history.json"));

        Assert.Contains("Failed", json);
        Assert.Equal(JobOutcome.Failed, Assert.Single(Store().All()).Outcome);
    }
}
