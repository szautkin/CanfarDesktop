using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// What the app is doing, in one place.
///
/// The three failures this exists to remove, all of which were observed rather than imagined: a
/// multi-stage probe reported as one boolean, so two rows both read "Discovering…" and neither said
/// which; outcomes dropped on the floor, so work that could not even be submitted looked exactly like
/// work nobody had asked for; and work stranded in a running state whenever a completion path was
/// missed.
///
/// One class, because the registry is global and these take turns with it.
/// </summary>
[Collection("TaskRegistry")]
public class TaskRegistryTests : IDisposable
{
    public TaskRegistryTests() => TaskRegistry.ResetForTests();

    public void Dispose() => TaskRegistry.ResetForTests();

    // ── Running, and finishing ──────────────────────────────────────────────────────────────────

    [Fact]
    public void StartedWorkIsRunningAndVisible()
    {
        using var task = TaskRegistry.Begin(TaskKind.Discovery, "Inspect skaha/base:1.0");

        var only = Assert.Single(TaskRegistry.Snapshot());
        Assert.Equal("Inspect skaha/base:1.0", only.Label);
        Assert.Equal(TaskKind.Discovery, only.Kind);
        Assert.Equal(TaskProgress.Running, only.Progress);
        Assert.Null(only.Finished);
        Assert.Equal(1, TaskRegistry.RunningCount());
    }

    [Fact]
    public void SucceedingRecordsIt()
    {
        var task = TaskRegistry.Begin(TaskKind.Launch, "Launch notebook");
        task.Succeed();

        Assert.Equal(TaskProgress.Succeeded, TaskRegistry.Snapshot()[0].Progress);
        Assert.NotNull(TaskRegistry.Snapshot()[0].Finished);
        Assert.Equal(0, TaskRegistry.RunningCount());
    }

    /// <summary>A failure with no reason is the thing this registry exists to stop.</summary>
    [Fact]
    public void AFailureKeepsItsReason()
    {
        var task = TaskRegistry.Begin(TaskKind.Storage, "Upload cube.fits");
        task.Fail("the service refused it: 413");

        var only = TaskRegistry.Snapshot()[0];
        Assert.Equal(TaskProgress.Failed, only.Progress);
        Assert.Equal("the service refused it: 413", only.Message);
        Assert.Equal(1, TaskRegistry.FailedCount());
    }

    /// <summary>
    /// A stage, not a boolean. "Inspect x" spends most of its life somewhere specific, and a reader who
    /// can see WHICH can tell a slow probe from a stuck one.
    /// </summary>
    [Fact]
    public void AStageSaysWhereTheWorkHasGotTo()
    {
        using var task = TaskRegistry.Begin(TaskKind.Discovery, "Inspect skaha/base:1.0");

        Assert.Equal(string.Empty, TaskRegistry.Snapshot()[0].Stage);

        task.Stage("waiting for job vi-abc");

        Assert.Equal("waiting for job vi-abc", TaskRegistry.Snapshot()[0].Stage);
        Assert.Equal(TaskProgress.Running, TaskRegistry.Snapshot()[0].Progress);
    }

    // ── Abandonment ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The mechanism: a handle that goes out of scope with no outcome records that nobody reported one.
    /// Without it, an early return or a closed dialog leaves a task reading as running for the rest of
    /// the session — which happened three separate times in one afternoon on the sibling app.
    /// </summary>
    [Fact]
    public void AnAbandonedTaskIsCancelledRatherThanLeftRunning()
    {
        Abandon();

        Assert.Equal(TaskProgress.Cancelled, TaskRegistry.Snapshot()[0].Progress);
        Assert.Equal(0, TaskRegistry.RunningCount());
        Assert.Equal(1, TaskRegistry.FailedCount());   // abandoned counts as gone wrong, not as fine

        // An early return inside a `using`, which is exactly the shape that used to strand work.
        static void Abandon()
        {
            using var task = TaskRegistry.Begin(TaskKind.Session, "Delete session abc");
            if (task is not null) return;
            task!.Succeed();
        }
    }

    /// <summary>
    /// `using var task = …; task.Succeed();` is the ordinary shape, and the disposal that follows must
    /// not overwrite the answer with "cancelled".
    /// </summary>
    [Fact]
    public void DisposingAfterSucceedingDoesNotUndoTheOutcome()
    {
        using (var task = TaskRegistry.Begin(TaskKind.Sync, "Reconcile"))
            task.Succeed();

        Assert.Equal(TaskProgress.Succeeded, TaskRegistry.Snapshot()[0].Progress);
        Assert.Equal(0, TaskRegistry.FailedCount());
    }

    [Fact]
    public void DisposingTwiceIsOneOutcome()
    {
        var task = TaskRegistry.Begin(TaskKind.Sync, "Reconcile");
        task.Fail("no");
        task.Dispose();
        task.Dispose();

        Assert.Equal(TaskProgress.Failed, TaskRegistry.Snapshot()[0].Progress);
        Assert.Equal("no", TaskRegistry.Snapshot()[0].Message);
    }

    // ── Staying bounded ─────────────────────────────────────────────────────────────────────────

    /// <summary>A catalogue sweep of three hundred probes must not grow this without limit.</summary>
    [Fact]
    public void FinishedWorkIsForgottenOnceThereIsTooMuchOfIt()
    {
        for (var i = 0; i < TaskRegistry.MaxTasks * 3; i++)
            TaskRegistry.Begin(TaskKind.Discovery, $"probe {i}").Succeed();

        Assert.True(TaskRegistry.Snapshot().Count <= TaskRegistry.MaxTasks);
    }

    /// <summary>
    /// A running task is the thing a reader most needs to see, so it is never evicted to make room for
    /// newer work — however much of that arrives.
    /// </summary>
    [Fact]
    public void RunningWorkSurvivesEvictionThatDropsFinishedWork()
    {
        using var keep = TaskRegistry.Begin(TaskKind.Storage, "Upload the big one");

        for (var i = 0; i < TaskRegistry.MaxTasks * 3; i++)
            TaskRegistry.Begin(TaskKind.Discovery, $"probe {i}").Succeed();

        Assert.Contains(TaskRegistry.Snapshot(), t => t.Label == "Upload the big one" && !t.IsFinished);
    }

    [Fact]
    public void ClearingFinishedLeavesTheRunningAlone()
    {
        using var running = TaskRegistry.Begin(TaskKind.Storage, "still going");
        TaskRegistry.Begin(TaskKind.Storage, "over").Succeed();

        TaskRegistry.ClearFinished();

        Assert.Equal("still going", Assert.Single(TaskRegistry.Snapshot()).Label);
    }

    [Fact]
    public void TheOldestIsFirstSoTheListReadsInTheOrderThingsHappened()
    {
        TaskRegistry.Begin(TaskKind.Storage, "first").Succeed();
        TaskRegistry.Begin(TaskKind.Storage, "second").Succeed();

        Assert.Equal(["first", "second"], TaskRegistry.Snapshot().Select(t => t.Label));
    }

    // ── Telling the UI ──────────────────────────────────────────────────────────────────────────

    /// <summary>The status bar polls this rather than copying the list on every frame.</summary>
    [Fact]
    public void EveryChangeMovesTheSequence()
    {
        var start = TaskRegistry.Sequence;

        var task = TaskRegistry.Begin(TaskKind.Sync, "x");
        var afterBegin = TaskRegistry.Sequence;
        Assert.True(afterBegin > start);

        task.Stage("halfway");
        var afterStage = TaskRegistry.Sequence;
        Assert.True(afterStage > afterBegin);

        task.Succeed();
        Assert.True(TaskRegistry.Sequence > afterStage);
    }

    [Fact]
    public void ChangedIsRaisedForEachChange()
    {
        var raised = 0;
        void OnChanged() => raised++;

        TaskRegistry.Changed += OnChanged;
        try
        {
            var task = TaskRegistry.Begin(TaskKind.Sync, "x");
            task.Stage("halfway");
            task.Succeed();
        }
        finally
        {
            TaskRegistry.Changed -= OnChanged;
        }

        Assert.Equal(3, raised);
    }

    /// <summary>
    /// A subscriber that throws must not take down the work it is being told about — the reporting is
    /// incidental to the operation, and an operation that failed because its progress bar threw would be
    /// a worse bug than the one this class fixes.
    /// </summary>
    [Fact]
    public void ASubscriberThatThrowsDoesNotBreakTheWork()
    {
        void Bad() => throw new InvalidOperationException("boom");

        TaskRegistry.Changed += Bad;
        try
        {
            var task = TaskRegistry.Begin(TaskKind.Sync, "x");
            task.Succeed();
        }
        finally
        {
            TaskRegistry.Changed -= Bad;
        }

        Assert.Equal(TaskProgress.Succeeded, TaskRegistry.Snapshot()[0].Progress);
    }

    // ── Elapsed ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>A finished task's elapsed time stops; a running one's keeps counting.</summary>
    [Fact]
    public void ElapsedStopsWhenTheWorkDoes()
    {
        var task = TaskRegistry.Begin(TaskKind.Sync, "x");
        Thread.Sleep(15);
        task.Succeed();

        var once = TaskRegistry.Snapshot()[0].Elapsed;
        Thread.Sleep(15);

        Assert.Equal(once, TaskRegistry.Snapshot()[0].Elapsed);
        Assert.True(once > TimeSpan.Zero);
    }
}

/// <summary>
/// The registry is one static thing, so everything that touches it runs one at a time. Without this,
/// another class's tasks land in this one's snapshot and the counts are whatever the scheduler decided.
/// </summary>
[CollectionDefinition("TaskRegistry", DisableParallelization = true)]
public class TaskRegistryCollection { }
