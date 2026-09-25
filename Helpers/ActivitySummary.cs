namespace CanfarDesktop.Helpers;

/// <summary>How one task reads in the expanded list: what it is, and where it got to.</summary>
public sealed record TaskLine(string Title, string Detail, TaskProgress Progress);

/// <summary>
/// Turning the task registry into the words on the status bar.
///
/// Separated from the bar itself because the wording is the part worth being sure of — "Idle" while
/// something is running, or a permanent "0 failed", are the ways a status bar becomes furniture — and a
/// control is the one thing a test cannot make.
///
/// Takes its translations through <see cref="Translate"/> rather than calling <c>Loc</c>, following
/// <c>AiGuideCatalog</c>: the resource loader needs a packaged app, and this has to be testable without
/// one. Unset, it answers in English.
/// </summary>
public static class ActivitySummary
{
    /// <summary>
    /// Resolves a resource key, or returns null to fall back to the English written here. Set once at
    /// startup, the same way the AI guide catalog takes its wording.
    /// </summary>
    public static Func<string, string?>? Translate { get; set; }

    private static string T(string key, string fallback) => Translate?.Invoke(key) ?? fallback;

    /// <summary>
    /// The one line an idle app costs.
    ///
    /// One running task names ITSELF — with the stage it has reached, which is the whole reason the
    /// registry records stages. Several become a count, because five labels on one line is not a line
    /// anybody reads.
    /// </summary>
    public static string Line(IReadOnlyList<TrackedTask> tasks)
    {
        var running = tasks.Where(t => !t.IsFinished).ToList();

        if (running.Count == 0) return T("Activity_Idle", "Idle");

        if (running.Count == 1)
        {
            var only = running[0];
            return only.Stage.Length > 0 ? $"{only.Label} — {only.Stage}" : only.Label;
        }

        return string.Format(T("Activity_RunningCount", "{0} tasks running"), running.Count);
    }

    /// <summary>
    /// How many went wrong, or null when none did.
    ///
    /// Null rather than "0 failed": a permanent zero is noise, and one that appears is information.
    /// </summary>
    public static string? Failures(IReadOnlyList<TrackedTask> tasks)
    {
        var failed = tasks.Count(t => t.Progress is TaskProgress.Failed or TaskProgress.Cancelled);

        return failed == 0 ? null : string.Format(T("Activity_FailedCount", "{0} failed"), failed);
    }

    /// <summary>
    /// The expanded list, newest first — the opposite of the registry's own order, because a list you
    /// have opened to find out what just happened should start with what just happened.
    /// </summary>
    public static IReadOnlyList<TaskLine> Lines(IReadOnlyList<TrackedTask> tasks)
        => tasks.Reverse().Select(Describe).ToList();

    /// <summary>
    /// One row: what it is, and where it got to.
    ///
    /// A failure shows its REASON. Anything else shows how long it took, or has been going — which is
    /// what separates a slow task from a stuck one.
    /// </summary>
    public static TaskLine Describe(TrackedTask task)
    {
        var detail = task.Progress switch
        {
            TaskProgress.Failed => task.Message is { Length: > 0 } why
                ? why
                : T("Activity_FailedNoReason", "failed"),

            // Not "cancelled": nobody chose this. The handle went away without an outcome, which means
            // a path returned early or a window closed, and saying so is more use than a bare word.
            TaskProgress.Cancelled => T("Activity_Abandoned", "abandoned before it finished"),

            TaskProgress.Succeeded => task.Message is { Length: > 0 } note
                ? $"{note} · {Duration(task.Elapsed)}"
                : Duration(task.Elapsed),

            _ => task.Stage.Length > 0
                ? $"{task.Stage} · {Duration(task.Elapsed)}"
                : Duration(task.Elapsed),
        };

        return new TaskLine(task.Label, detail, task.Progress);
    }

    /// <summary>
    /// How long, at the precision a person reads rather than the one a clock keeps.
    ///
    /// Sub-second work is "just now" — the exact millisecond count of something that already finished is
    /// noise, and a number that changes eight times a second is unreadable anyway.
    /// </summary>
    public static string Duration(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.FromSeconds(1)) return T("Activity_JustNow", "just now");
        if (elapsed < TimeSpan.FromMinutes(1)) return $"{(int)elapsed.TotalSeconds}s";
        if (elapsed < TimeSpan.FromHours(1)) return $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s";

        return $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m";
    }
}
