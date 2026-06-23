using System.Text.Json;
using CanfarDesktop.Models.ImageDiscovery;

namespace CanfarDesktop.Services.ImageDiscovery;

public interface IManifestStore
{
    LastOutcome? Outcome(string imageID);
    void SetManifest(ImageManifest manifest);
    void SetFailure(string imageID, FailureCategory category, string message, DateTimeOffset attemptedAt, string? jobID);
    void Invalidate(string imageID);
    void Clear();
    IReadOnlyList<string> Search(PackageQuery query);
    IReadOnlyList<PartialMatch> SearchPartial(PackageQuery query, double minScore, int limit);
    IReadOnlyList<string> KnownImages();
    AllPackages AllPackages();
    int Count();
}

/// <summary>
/// Per-image JSON cache of discovery outcomes: one file per image id at
/// <c>&lt;directory&gt;/&lt;sanitized-id&gt;.json</c> holding the full <see cref="LastOutcome"/>, mirrored
/// in memory for fast intersect-style queries. Writes are atomic (temp + replace); reads hydrate
/// lazily. Per-image granularity lets concurrent probes write without contending on one file.
/// </summary>
public class JsonManifestStore : IManifestStore
{
    private readonly string _directory;
    private readonly object _gate = new();
    private readonly Dictionary<string, LastOutcome> _loaded = new();
    private bool _hydrated;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public JsonManifestStore(string directory) => _directory = directory;

    private void EnsureHydrated()
    {
        if (_hydrated) return;
        _hydrated = true;

        try { Directory.CreateDirectory(_directory); }
        catch { return; }

        string[] files;
        try { files = Directory.GetFiles(_directory, "*.json"); }
        catch { return; }

        foreach (var file in files)
        {
            try
            {
                var outcome = JsonSerializer.Deserialize<LastOutcome>(File.ReadAllText(file), Options);
                if (outcome is not null && !string.IsNullOrEmpty(outcome.ImageID))
                    _loaded[outcome.ImageID] = outcome;
            }
            catch
            {
                // Skip unreadable cache file — never throw on hydration.
            }
        }
    }

    public LastOutcome? Outcome(string imageID)
    {
        lock (_gate) { EnsureHydrated(); return _loaded.GetValueOrDefault(imageID); }
    }

    public void SetManifest(ImageManifest manifest)
    {
        lock (_gate)
        {
            EnsureHydrated();
            var outcome = LastOutcome.Success(manifest);
            Persist(outcome, manifest.ImageID);
            _loaded[manifest.ImageID] = outcome;
        }
    }

    public void SetFailure(string imageID, FailureCategory category, string message, DateTimeOffset attemptedAt, string? jobID)
    {
        lock (_gate)
        {
            EnsureHydrated();
            var outcome = LastOutcome.Failure(imageID, category, message, attemptedAt, jobID);
            Persist(outcome, imageID);
            _loaded[imageID] = outcome;
        }
    }

    public void Invalidate(string imageID)
    {
        lock (_gate)
        {
            EnsureHydrated();
            _loaded.Remove(imageID);
            var path = FilePath(imageID);
            if (File.Exists(path)) { try { File.Delete(path); } catch { /* best effort */ } }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            EnsureHydrated();
            _loaded.Clear();
            if (!Directory.Exists(_directory)) return;
            foreach (var f in Directory.GetFiles(_directory, "*.json"))
            {
                try { File.Delete(f); } catch { /* best effort */ }
            }
        }
    }

    public IReadOnlyList<string> Search(PackageQuery query)
    {
        lock (_gate)
        {
            EnsureHydrated();
            var ids = new List<string>();
            foreach (var (id, outcome) in _loaded)
                if (outcome.IsSuccess && outcome.Manifest is { } m && (query.IsEmpty || query.Matches(m)))
                    ids.Add(id);
            ids.Sort(StringComparer.Ordinal);
            return ids;
        }
    }

    public IReadOnlyList<PartialMatch> SearchPartial(PackageQuery query, double minScore, int limit)
    {
        lock (_gate)
        {
            EnsureHydrated();
            if (query.IsEmpty) return Array.Empty<PartialMatch>();

            var results = new List<PartialMatch>();
            foreach (var (id, outcome) in _loaded)
            {
                if (!outcome.IsSuccess || outcome.Manifest is not { } m) continue;
                var (score, missing) = query.Score(m);
                if (score >= minScore) results.Add(new PartialMatch(id, score, missing));
            }
            results.Sort((a, b) => a.Score != b.Score ? b.Score.CompareTo(a.Score) : string.CompareOrdinal(a.ImageID, b.ImageID));
            return results.Count > limit ? results.GetRange(0, limit) : results;
        }
    }

    public IReadOnlyList<string> KnownImages()
    {
        lock (_gate)
        {
            EnsureHydrated();
            var keys = _loaded.Keys.ToList();
            keys.Sort(StringComparer.Ordinal);
            return keys;
        }
    }

    public AllPackages AllPackages()
    {
        lock (_gate)
        {
            EnsureHydrated();
            var result = new AllPackages();
            foreach (var outcome in _loaded.Values)
            {
                if (!outcome.IsSuccess || outcome.Manifest is not { } m) continue;
                if (m.OsFamily != "unknown")
                {
                    result.OsFamilies.Add(m.OsFamily);
                    if (m.OsVersion != "unknown")
                    {
                        if (!result.OsVersionsByFamily.TryGetValue(m.OsFamily, out var set))
                        {
                            set = new HashSet<string>();
                            result.OsVersionsByFamily[m.OsFamily] = set;
                        }
                        set.Add(m.OsVersion);
                    }
                }
                foreach (var p in m.DpkgPackages) result.Dpkg.Add(p.Name);
                foreach (var p in m.RpmPackages) result.Rpm.Add(p.Name);
                foreach (var p in m.ApkPackages) result.Apk.Add(p.Name);
                foreach (var p in m.PythonPackages) result.Python.Add(p.Name);
                foreach (var p in m.RPackages) result.R.Add(p.Name);
            }
            return result;
        }
    }

    public int Count()
    {
        lock (_gate) { EnsureHydrated(); return _loaded.Count; }
    }

    private string FilePath(string imageID) => Path.Combine(_directory, ImageManifest.Sanitize(imageID) + ".json");

    private void Persist(LastOutcome outcome, string imageID)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var path = FilePath(imageID);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(outcome, Options));
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"JsonManifestStore persist failed for {imageID}: {ex.Message}");
        }
    }
}
