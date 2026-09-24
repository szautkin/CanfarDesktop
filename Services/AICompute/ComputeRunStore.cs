using System.Text.Json;
using System.Text.Json.Serialization;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models.AICompute;

namespace CanfarDesktop.Services.AICompute;

/// <summary>The code sent to the remote compute session, and what came of it.</summary>
public interface IComputeRunStore
{
    /// <summary>Every remembered run, newest first.</summary>
    IReadOnlyList<ComputeRun> All();

    ComputeRun? Find(string id);

    /// <summary>Remember a run that has just been sent.</summary>
    void Add(ComputeRun run);

    /// <summary>Record the watcher's result for a run. A run the store does not know is left alone.</summary>
    void Complete(string id, RunCodeResult result);

    /// <summary>Close a run the app has stopped waiting for, with <see cref="ComputeRun.NoResult"/> or <see cref="ComputeRun.NotSent"/>.</summary>
    void Close(string id, string status);

    void Clear();

    /// <summary>Raised after any change, so the Remote Compute screen can redraw.</summary>
    event Action? Changed;
}

/// <summary>
/// The last few dozen compute runs, on disk.
///
/// <para>Before this, code an agent ran on somebody's account left no trace they could find: the request
/// and result files sit in a hidden folder in VOSpace, and nothing in the app listed them. This is what
/// the Remote Compute screen shows, and what <c>list_compute_runs</c> answers from.</para>
/// </summary>
public sealed class ComputeRunStore : IComputeRunStore
{
    private const string FileName = "compute_runs.json";
    private const int SchemaVersion = 1;

    /// <summary>How many runs to keep. The code is kept with each, so this also bounds the file.</summary>
    public const int MaxRuns = 50;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string? _filePath;

    /// <summary>
    /// Serialises read-modify-write: a run finishing while another is being added would otherwise drop
    /// one of the two.
    /// </summary>
    private readonly object _gate = new();

    public event Action? Changed;

    public ComputeRunStore()
    {
        try
        {
            _filePath = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, FileName);
        }
        catch
        {
            // Unpackaged — the runs live for the session, as the other local stores do.
        }
    }

    /// <summary>A store at an explicit path, for tests.</summary>
    public ComputeRunStore(string filePath) => _filePath = filePath;

    public IReadOnlyList<ComputeRun> All()
    {
        lock (_gate) return Load();
    }

    public ComputeRun? Find(string id)
    {
        lock (_gate) return Load().FirstOrDefault(r => SameId(r.Id, id));
    }

    public void Add(ComputeRun run)
    {
        if (string.IsNullOrWhiteSpace(run.Id)) return;

        lock (_gate)
        {
            var runs = Load();
            runs.RemoveAll(r => SameId(r.Id, run.Id));
            runs.Insert(0, run);
            if (runs.Count > MaxRuns) runs.RemoveRange(MaxRuns, runs.Count - MaxRuns);
            Save(runs);
        }

        Changed?.Invoke();
    }

    public void Complete(string id, RunCodeResult result)
        => Update(id, run => run with
        {
            Status = string.IsNullOrWhiteSpace(result.Status) ? "error" : result.Status.Trim().ToLowerInvariant(),
            ExitCode = result.ExitCode,
            DurationMs = result.DurationMs,
            FinishedAt = result.FinishedAt ?? IsoTime.Now(),
        });

    public void Close(string id, string status)
        => Update(id, run => run.IsFinished ? run : run with { Status = status, FinishedAt = IsoTime.Now() });

    public void Clear()
    {
        lock (_gate) Save([]);
        Changed?.Invoke();
    }

    /// <summary>Change one run in place — it keeps its position, which is when it was sent.</summary>
    private void Update(string id, Func<ComputeRun, ComputeRun> change)
    {
        bool changed;
        lock (_gate)
        {
            var runs = Load();
            var at = runs.FindIndex(r => SameId(r.Id, id));
            var updated = at >= 0 ? change(runs[at]) : null;

            // Every poll after a run finishes reads the same result again; writing it again would
            // rewrite the file and redraw the list for nothing.
            changed = updated is not null && updated != runs[at];
            if (changed)
            {
                runs[at] = updated!;
                Save(runs);
            }
        }

        if (changed) Changed?.Invoke();
    }

    private static bool SameId(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private List<ComputeRun> Load()
        => DiskPersistence.Read(_filePath, SchemaVersion, () => new List<ComputeRun>(), Json).Value;

    private void Save(List<ComputeRun> runs) => DiskPersistence.Write(_filePath, runs, SchemaVersion, Json);
}
