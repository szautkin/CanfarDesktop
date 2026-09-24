namespace CanfarDesktop.Models.AICompute;

/// <summary>Who asked for a run: an AI agent through <c>run_code</c>, or the person at the Remote Compute screen.</summary>
public enum ComputeRunAuthor { Agent, User }

public static class ComputeRunAuthorNames
{
    /// <summary>How an author is named to an agent: <c>agent</c> or <c>user</c>, as marks name theirs.</summary>
    public static string Name(this ComputeRunAuthor author) => author == ComputeRunAuthor.Agent ? "agent" : "user";
}

/// <summary>
/// One piece of code sent to the remote compute session, as the app remembers it.
///
/// <para>The watcher's request and result files live in VOSpace and say nothing about who asked: their
/// format is shared byte for byte with the watcher and with the macOS app. Who asked is a local fact, so
/// it is kept here. The output is not copied — it can run to a megabyte — and is read from the result
/// file when somebody opens the run.</para>
/// </summary>
public sealed record ComputeRun(
    string Id, ComputeRunAuthor Author, string Language, string Code, int TimeoutSeconds, string SubmittedAt)
{
    /// <summary>A run that is still out — sent, and nothing back yet.</summary>
    public const string Running = "running";

    /// <summary>Set by the app: nothing came back in the time the run could have taken.</summary>
    public const string NoResult = "noResult";

    /// <summary>Set by the app: the request never reached the session.</summary>
    public const string NotSent = "notSent";

    /// <summary>
    /// The watcher's verdict — <c>ok</c>, <c>error</c> or <c>timeout</c> — or one of the app's own,
    /// <see cref="NoResult"/> and <see cref="NotSent"/>. Null while the run is still out.
    /// </summary>
    public string? Status { get; init; }

    public int? ExitCode { get; init; }
    public long? DurationMs { get; init; }
    public string? FinishedAt { get; init; }

    /// <summary>Whether anything more is going to happen to this run.</summary>
    public bool IsFinished => Status is not null;

    /// <summary>Where the run stands, including while it is still out.</summary>
    public string State => Status ?? Running;
}
