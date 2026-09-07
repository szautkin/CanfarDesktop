namespace CanfarDesktop.Helpers;

/// <summary>What kind of work it is, for grouping and for the icon.</summary>
public enum TaskKind
{
    /// <summary>Inspecting a container image.</summary>
    Discovery,

    /// <summary>Launching a session or batch job.</summary>
    Launch,

    /// <summary>Acting on an existing session.</summary>
    Session,

    /// <summary>Reading or writing VOSpace.</summary>
    Storage,

    /// <summary>Background reconciliation.</summary>
    Sync,
}

/// <summary>Where a task got to.</summary>
public enum TaskProgress
{
    Running,
    Succeeded,

    /// <summary>Carries a reason — a failure with no reason is the thing this registry exists to stop.</summary>
    Failed,

    /// <summary>The handle went away without an outcome: the work was abandoned.</summary>
    Cancelled,
}

/// <summary>One unit of work, as the status bar shows it.</summary>
public sealed record TrackedTask(
    long Id,
    TaskKind Kind,
    string Label,
    string Stage,
    TaskProgress Progress,
    string? Message,
    DateTimeOffset Started,
    DateTimeOffset? Finished)
{
    public bool IsFinished => Progress != TaskProgress.Running;

    /// <summary>How long it ran, or has been running.</summary>
    public TimeSpan Elapsed => (Finished ?? DateTimeOffset.UtcNow) - Started;
}

/// <summary>
/// What the app is doing right now, in one place.
///
/// Every long operation is a detached call with whatever feedback the control that started it happened
/// to offer: a row subtitle here, a status label there, an InfoBar, or — often — nothing. So a probe
/// three minutes into waiting on a Skaha job and one that failed to submit at all look identical from
/// outside the control that started them, and if that control was a dialog you have since closed, they
/// look like nothing at all.
///
/// One registry, a STAGE rather than a boolean, and an outcome recorded even when nobody reports one, so
/// "running forever" is not a state the app can reach by omission.
///
/// Global by design, like the app's other cross-cutting logs: every surface that starts slow work can
/// reach it without being handed a dependency, and the status bar can read all of it in one place.
/// </summary>
public static class TaskRegistry
{
    /// <summary>
    /// How many to remember.
    ///
    /// Finished ones are kept so a failure can be read after the fact — an InfoBar that has been
    /// dismissed is no record. Bounded so a catalogue sweep of three hundred probes cannot grow it
    /// without limit.
    /// </summary>
    public const int MaxTasks = 60;

    private static readonly List<TrackedTask> Tasks = new();
    private static readonly object Gate = new();
    private static long _nextId;
    private static long _sequence;

    /// <summary>
    /// Bumped on every change, so a reader can tell "nothing happened" from "something did" without
    /// copying the list.
    /// </summary>
    public static long Sequence => Interlocked.Read(ref _sequence);

    /// <summary>
    /// Raised after every change, on whatever thread made it — a background download, a worker, the UI.
    /// Subscribers marshal for themselves.
    /// </summary>
    public static event Action? Changed;

    /// <summary>Start tracking a piece of work. The returned handle owns its outcome.</summary>
    public static TaskHandle Begin(TaskKind kind, string label)
    {
        var id = Interlocked.Increment(ref _nextId);
        lock (Gate)
        {
            Tasks.Add(new TrackedTask(id, kind, label, string.Empty, TaskProgress.Running, null,
                DateTimeOffset.UtcNow, null));

            // Evict the oldest FINISHED entries first. A running task is the thing a reader most needs
            // to see, and must never be dropped to make room for newer work.
            while (Tasks.Count > MaxTasks)
            {
                var at = Tasks.FindIndex(t => t.IsFinished);
                if (at < 0) break;
                Tasks.RemoveAt(at);
            }
        }

        Bump();
        return new TaskHandle(id);
    }

    /// <summary>A snapshot for the UI, oldest first.</summary>
    public static IReadOnlyList<TrackedTask> Snapshot()
    {
        lock (Gate) return Tasks.ToList();
    }

    /// <summary>How many are still running.</summary>
    public static int RunningCount()
    {
        lock (Gate) return Tasks.Count(t => !t.IsFinished);
    }

    /// <summary>How many finished badly — failed, or abandoned.</summary>
    public static int FailedCount()
    {
        lock (Gate) return Tasks.Count(t => t.Progress is TaskProgress.Failed or TaskProgress.Cancelled);
    }

    /// <summary>Forget everything that has finished, leaving the running ones alone.</summary>
    public static void ClearFinished()
    {
        lock (Gate) Tasks.RemoveAll(t => t.IsFinished);
        Bump();
    }

    /// <summary>For tests: empty it. Not for app code — a cleared registry loses running work.</summary>
    internal static void ResetForTests()
    {
        lock (Gate) Tasks.Clear();
        Bump();
    }

    internal static void SetStage(long id, string stage)
    {
        lock (Gate)
        {
            var at = Tasks.FindIndex(t => t.Id == id);
            if (at < 0) return;
            Tasks[at] = Tasks[at] with { Stage = stage };
        }

        Bump();
    }

    internal static void Finish(long id, TaskProgress progress, string? message)
    {
        lock (Gate)
        {
            var at = Tasks.FindIndex(t => t.Id == id);
            if (at < 0) return;

            // First outcome wins, so a task finished explicitly is not rewritten by anything after it.
            if (Tasks[at].IsFinished) return;

            Tasks[at] = Tasks[at] with
            {
                Progress = progress,
                Message = message,
                Finished = DateTimeOffset.UtcNow,
            };
        }

        Bump();
    }

    private static void Bump()
    {
        Interlocked.Increment(ref _sequence);
        try { Changed?.Invoke(); }
        catch { /* a broken subscriber must not take down the work it is being told about */ }
    }
}

/// <summary>
/// A running task. Report its outcome, or letting the handle go records that nobody did.
///
/// <c>using var task = TaskRegistry.Begin(…)</c> is the intended shape: disposal at the end of the scope
/// is what makes an abandoned task — an early return, a thrown exception, a closed dialog — record
/// itself as cancelled instead of reading as running for the rest of the session. That is the whole
/// mechanism, and it is why this is disposable rather than a plain id.
///
/// The finalizer is a backstop for a handle nobody disposed. It records the same thing, but a garbage
/// collector decides when — so it is a net, not the design.
/// </summary>
public sealed class TaskHandle : IDisposable
{
    private readonly long _id;
    private int _finished;

    internal TaskHandle(long id) => _id = id;

    ~TaskHandle() => Complete(TaskProgress.Cancelled, null);

    /// <summary>
    /// Say where the work has got to.
    ///
    /// The whole point: "Inspect skaha/base:1.0" spends most of its life somewhere specific — looking
    /// for a published manifest, waiting on a job — and a reader who can see WHICH can tell a slow probe
    /// from a stuck one.
    /// </summary>
    public void Stage(string stage) => TaskRegistry.SetStage(_id, stage);

    /// <summary>It worked.</summary>
    public void Succeed(string? message = null) => Complete(TaskProgress.Succeeded, message);

    /// <summary>It did not, and this is why.</summary>
    public void Fail(string why) => Complete(TaskProgress.Failed, why);

    /// <summary>Nobody said how it went, which is itself worth recording.</summary>
    public void Dispose() => Complete(TaskProgress.Cancelled, null);

    private void Complete(TaskProgress progress, string? message)
    {
        // One outcome per handle: succeed-then-dispose is the ordinary shape of a `using`, and the
        // disposal must not overwrite the answer with "cancelled".
        if (Interlocked.Exchange(ref _finished, 1) != 0) return;

        GC.SuppressFinalize(this);
        TaskRegistry.Finish(_id, progress, message);
    }
}
