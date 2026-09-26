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

    /// <summary>
    /// Raised after every change, on whatever thread made it — often a download finishing in the
    /// background, long after the screen that started it moved on. Subscribers marshal for themselves.
    /// </summary>
    public event Action? Changed;

    private void RaiseChanged()
    {
        try { Changed?.Invoke(); }
        catch { /* a broken subscriber must not undo the save it is being told about */ }
    }

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

    /// <summary>
    /// Save, replacing the record for the same PRODUCT — the same observation, and the same cutout of it
    /// or likewise none. It used to replace by publisher id alone, which would have let a cutout
    /// overwrite the complete download it was cut from.
    /// </summary>
    public void Save(DownloadedObservation observation)
    {
        lock (_lock)
        {
            _observations.RemoveAll(o => o.PublisherID == observation.PublisherID && o.ProductKey == observation.ProductKey);
            _observations.Insert(0, observation);
            WriteToDisk();
        }
        RaiseChanged();
    }

    /// <summary>
    /// Save this product only if Research does not have it yet — for keeping an observation without
    /// downloading it. Never over a record that exists: that one may hold a downloaded file, and a
    /// record saved without one would forget it. True when it was saved.
    /// </summary>
    public bool SaveIfAbsent(DownloadedObservation observation)
    {
        lock (_lock)
        {
            if (_observations.Any(o => o.PublisherID == observation.PublisherID && o.ProductKey == observation.ProductKey))
                return false;
            _observations.Insert(0, observation);
            WriteToDisk();
        }
        RaiseChanged();
        return true;
    }

    /// <summary>Whether Research holds this product of an observation — the complete one when <paramref name="productKey"/> is null.</summary>
    public bool Has(string publisherId, string? productKey = null)
    {
        lock (_lock) return _observations.Any(o => o.PublisherID == publisherId && o.ProductKey == productKey);
    }

    public void Remove(DownloadedObservation observation)
    {
        lock (_lock)
        {
            _observations.RemoveAll(o => o.Id == observation.Id);
            WriteToDisk();
        }
        RaiseChanged();
    }

    public void Clear()
    {
        lock (_lock)
        {
            _observations.Clear();
            WriteToDisk();
        }
        RaiseChanged();
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

        if (all.FirstOrDefault(o => o.Id == id) is { } byId) return byId;

        // A publisher id can hold the complete observation and cutouts of it; asked for the
        // observation, the complete one is the answer. A local id names a cutout exactly.
        var byPublisher = all.Where(o => o.PublisherID == id).ToList();
        if (byPublisher.Count > 0) return byPublisher.FirstOrDefault(o => !o.IsCutout) ?? byPublisher[0];

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
