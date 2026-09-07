using System.Text.Json;
using System.Text.Json.Serialization;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;

namespace CanfarDesktop.Services;

/// <summary>What the app remembers about jobs CANFAR no longer has.</summary>
public interface IJobHistoryStore
{
    /// <summary>Every remembered job, newest first.</summary>
    IReadOnlyList<JobRecord> All();

    /// <summary>Remember a finished job, replacing any earlier record of the same id.</summary>
    void Record(JobRecord job);

    /// <summary>Forget everything.</summary>
    void Clear();

    /// <summary>Raised after the history changes, so a card showing it can redraw.</summary>
    event Action? Changed;
}

/// <summary>
/// The last few dozen finished batch jobs, on disk.
///
/// CANFAR reaps headless jobs, and the image-discovery coordinator deletes its own probes the moment
/// they finish, so the live sessions listing is the wrong place to look for what happened. A job could
/// fail and, a minute later, the app had nothing to say about it: gone from the listing, logs and events
/// gone with it, and the only trace a count that had ticked from Running to Failed.
///
/// This keeps what the listing forgets — the outcome, when, and for failures the reason, captured while
/// the job still existed.
/// </summary>
public sealed class JobHistoryStore : IJobHistoryStore
{
    private const string FileName = "job_history.json";
    private const int SchemaVersion = 1;

    /// <summary>
    /// How many finished jobs to keep.
    ///
    /// Enough that a morning of failed probes does not push the interesting one off the end, and small
    /// enough that the file stays a few tens of kilobytes.
    /// </summary>
    public const int MaxJobs = 50;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string? _filePath;

    /// <summary>
    /// Serialises read-modify-write. Two pollers finishing at once would otherwise each read the old
    /// list and the second would drop the first's entry.
    /// </summary>
    private readonly object _gate = new();

    public event Action? Changed;

    public JobHistoryStore()
    {
        try
        {
            _filePath = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, FileName);
        }
        catch
        {
            // Unpackaged — the history lives for the session, as the other local stores do.
        }
    }

    /// <summary>A store at an explicit path, for tests.</summary>
    public JobHistoryStore(string filePath) => _filePath = filePath;

    public IReadOnlyList<JobRecord> All()
    {
        lock (_gate) return Load();
    }

    /// <summary>
    /// Remember a finished job.
    ///
    /// Replacing an existing id rather than skipping it matters: the poller may notice a job failed
    /// before the reason has been fetched, and the record that carries the reason has to win.
    /// </summary>
    public void Record(JobRecord job)
    {
        if (string.IsNullOrWhiteSpace(job.Id)) return;

        lock (_gate)
        {
            var jobs = Load();
            jobs.RemoveAll(j => string.Equals(j.Id, job.Id, StringComparison.OrdinalIgnoreCase));

            // Newest first: what just happened is what somebody opening this is looking for.
            jobs.Insert(0, job.FinishedAt.Length > 0
                ? job
                : job with { FinishedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") });

            if (jobs.Count > MaxJobs) jobs.RemoveRange(MaxJobs, jobs.Count - MaxJobs);
            Save(jobs);
        }

        Changed?.Invoke();
    }

    public void Clear()
    {
        lock (_gate) Save([]);
        Changed?.Invoke();
    }

    private List<JobRecord> Load()
        => DiskPersistence.Read(_filePath, SchemaVersion, () => new List<JobRecord>(), Json).Value;

    private void Save(List<JobRecord> jobs)
    {
        if (_filePath is null) return;

        // DiskPersistence writes the file but does not make the folder it lives in.
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        DiskPersistence.Write(_filePath, jobs, SchemaVersion, Json);
    }
}
