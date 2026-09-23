using System.Text.Json;
using Windows.Storage;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;

namespace CanfarDesktop.Services;

/// <summary>
/// Persists downloaded observation metadata to JSON on disk via a versioned, resilient
/// envelope (corrupt files are quarantined, newer-schema files are not clobbered).
/// Missing-file observations are kept, not pruned, so metadata survives offline/remounted volumes.
/// </summary>
public class ObservationStore
{
    private const string FileName = "downloaded_observations.json";
    private const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string? _filePath;
    private readonly object _lock = new();
    private List<DownloadedObservation> _observations = [];

    public IReadOnlyList<DownloadedObservation> Observations { get { lock (_lock) return _observations.ToList(); } }
    public int Count { get { lock (_lock) return _observations.Count; } }

    public ObservationStore()
    {
        try
        {
            var folder = ApplicationData.Current.LocalFolder.Path;
            _filePath = Path.Combine(folder, FileName);
            // Do NOT prune missing-file observations — preserve metadata for offline/remounted volumes.
            _observations = DiskPersistence.Read(_filePath, SchemaVersion,
                () => new List<DownloadedObservation>(), JsonOptions).Value;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ObservationStore init failed: {ex.Message}");
            _filePath = null;
        }
    }

    public void Save(DownloadedObservation observation)
    {
        lock (_lock)
        {
            _observations.RemoveAll(o => o.PublisherID == observation.PublisherID);
            _observations.Insert(0, observation);
            WriteToDisk();
        }
    }

    public void Remove(DownloadedObservation observation)
    {
        lock (_lock)
        {
            _observations.RemoveAll(o => o.Id == observation.Id);
            WriteToDisk();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _observations.Clear();
            WriteToDisk();
        }
    }

    /// <summary>A downloaded observation by any of the ids a caller is likely to hold.</summary>
    public DownloadedObservation? Find(string? id) => Match(Observations, id);

    /// <summary>
    /// A downloaded observation by its local id, its publisher id, or the archive's observation id.
    ///
    /// <para>The archive's id is what Search shows and what a person reads out ("1832496"), so asking
    /// for it and being told the observation is not downloaded — when it is — was wrong. It is taken
    /// only when it picks out one download: the same observation id can exist in two collections, and
    /// a guess between them opens the wrong file.</para>
    /// </summary>
    public static DownloadedObservation? Match(IReadOnlyList<DownloadedObservation> all, string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        id = id.Trim();

        var exact = all.FirstOrDefault(o => o.Id == id || o.PublisherID == id);
        if (exact is not null) return exact;

        var byArchiveId = all.Where(o => string.Equals(o.ObservationID, id, StringComparison.OrdinalIgnoreCase))
                             .Take(2).ToList();
        return byArchiveId.Count == 1 ? byArchiveId[0] : null;
    }

    public bool Contains(string publisherID)
    {
        lock (_lock) return _observations.Any(o => o.PublisherID == publisherID);
    }

    public Dictionary<string, List<DownloadedObservation>> GroupByCollection()
    {
        lock (_lock)
            return _observations
                .GroupBy(o => o.Collection)
                .OrderBy(g => g.Key)
                .ToDictionary(g => g.Key, g => g.ToList());
    }

    public List<DownloadedObservation> Filter(string text)
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(text)) return _observations.ToList();
            return _observations.Where(o =>
                o.TargetName.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                o.Collection.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                o.Instrument.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                o.ObservationID.Contains(text, StringComparison.OrdinalIgnoreCase)
            ).ToList();
        }
    }

    private void WriteToDisk()
    {
        if (_filePath is null) return;
        DiskPersistence.Write(_filePath, _observations, SchemaVersion, JsonOptions);
    }
}
