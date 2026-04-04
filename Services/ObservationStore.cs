using System.Text.Json;
using Windows.Storage;
using CanfarDesktop.Models;

namespace CanfarDesktop.Services;

/// <summary>
/// Persists downloaded observation metadata to JSON on disk.
/// Validates file existence on load, removes stale entries.
/// </summary>
public class ObservationStore
{
    private const string FileName = "downloaded_observations.json";
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
            _observations = ReadFromDisk();

            var before = _observations.Count;
            _observations.RemoveAll(o => !o.FileExists);
            if (_observations.Count != before)
                WriteToDisk();
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

    private List<DownloadedObservation> ReadFromDisk()
    {
        if (_filePath is null || !File.Exists(_filePath)) return [];
        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<DownloadedObservation>>(json, JsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ObservationStore read failed: {ex.Message}");
            return [];
        }
    }

    private void WriteToDisk()
    {
        if (_filePath is null) return;
        try
        {
            File.WriteAllText(_filePath, JsonSerializer.Serialize(_observations, JsonOptions));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ObservationStore write failed: {ex.Message}");
        }
    }
}
