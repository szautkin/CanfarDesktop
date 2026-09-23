namespace CanfarDesktop.Mcp.Tools.Proposals;

/// <summary>Where a job has got to.</summary>
public enum JobStatus { Running, Succeeded, Failed }

/// <summary>One background job.</summary>
/// <param name="Id">The proposal this is applying.</param>
/// <param name="Kind">The proposal kind, e.g. <c>download_observation</c>.</param>
/// <param name="Summary">
/// The proposal's one-line summary, so a caller listing jobs can tell them apart without having to
/// hold onto what it asked for.
/// </param>
/// <param name="Message">The applier's own words: its result on success, its error on failure.</param>
public sealed record BackgroundJob(
    string Id,
    string Kind,
    string Summary,
    JobStatus Status,
    string StartedAt,
    string? FinishedAt = null,
    string? Message = null)
{
    /// <summary>Whether this job has stopped, either way.</summary>
    public bool IsFinished => Status != JobStatus.Running;
}

/// <summary>
/// Background jobs an agent can start and then ask about.
///
/// Some tool calls take longer than a JSON-RPC request should be held open. A large observation
/// download is one: the apply is awaited inside the call, the MCP client gives up at its own timeout,
/// and the work vanishes with no id, no progress and no error — the caller cannot even tell whether it
/// is still running. The VOSpace tools' descriptions currently say as much, and advise re-polling a
/// different tool and hoping.
///
/// So a long apply can be started as a JOB, answering immediately with an id, and its state lives here
/// until someone asks.
///
/// Keyed by the PROPOSAL id rather than a fresh one: the proposal already identifies the piece of work,
/// the agent already has it in the reply, and one identifier is easier to follow than two.
/// </summary>
public sealed class JobRegistry
{
    /// <summary>How many finished jobs to keep. Running ones are never evicted.</summary>
    private const int MaxRemembered = 50;

    private readonly object _gate = new();
    private readonly List<BackgroundJob> _jobs = new();

    private static string Now() => CanfarDesktop.Helpers.IsoTime.Now();

    /// <summary>Record a job as running. Returns it, so the caller can answer with its id at once.</summary>
    public BackgroundJob Start(string id, string kind, string summary)
    {
        var job = new BackgroundJob(id, kind, summary, JobStatus.Running, Now());

        lock (_gate)
        {
            // Restarting an id replaces its record rather than adding a second: two rows with one id is
            // a list a caller cannot read, and the newer attempt is the one being asked about.
            _jobs.RemoveAll(j => j.Id == id);
            _jobs.Add(job);
            Evict();
        }

        return job;
    }

    /// <summary>Mark a job finished, with the applier's own words. Ignored when the id is unknown.</summary>
    public void Finish(string id, bool succeeded, string? message)
    {
        lock (_gate)
        {
            var at = _jobs.FindIndex(j => j.Id == id);
            if (at < 0) return;

            _jobs[at] = _jobs[at] with
            {
                Status = succeeded ? JobStatus.Succeeded : JobStatus.Failed,
                FinishedAt = Now(),
                Message = message,
            };
            Evict();
        }
    }

    /// <summary>One job by id, or null.</summary>
    public BackgroundJob? Get(string id)
    {
        lock (_gate) return _jobs.FirstOrDefault(j => j.Id == id);
    }

    /// <summary>Every job, newest first.</summary>
    public IReadOnlyList<BackgroundJob> All()
    {
        lock (_gate) return _jobs.AsEnumerable().Reverse().ToList();
    }

    /// <summary>How many are still going — what a caller checks before it decides to wait.</summary>
    public int RunningCount()
    {
        lock (_gate) return _jobs.Count(j => !j.IsFinished);
    }

    /// <summary>
    /// Forget the oldest FINISHED jobs past the cap. A running one is never evicted whatever the cap
    /// says: dropping it would leave work in flight that nothing can report on, which is the exact
    /// problem this class exists to prevent.
    /// </summary>
    private void Evict()
    {
        var finished = _jobs.Count(j => j.IsFinished);
        if (finished <= MaxRemembered) return;

        var toDrop = finished - MaxRemembered;
        for (var i = 0; i < _jobs.Count && toDrop > 0; )
        {
            if (_jobs[i].IsFinished)
            {
                _jobs.RemoveAt(i);
                toDrop--;
            }
            else i++;
        }
    }
}
