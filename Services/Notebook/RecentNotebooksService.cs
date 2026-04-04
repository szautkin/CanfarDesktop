namespace CanfarDesktop.Services.Notebook;

using System.Text.Json;

/// <summary>
/// Persists a list of recently opened .ipynb files to %LocalAppData%.
/// Singleton service. Thread-safe via lock.
/// </summary>
public class RecentNotebooksService
{
    private const int MaxRecent = 15;
    private readonly string _filePath;
    private readonly object _lock = new();
    private List<RecentNotebookEntry> _entries = [];

    public IReadOnlyList<RecentNotebookEntry> Entries
    {
        get { lock (_lock) return _entries.AsReadOnly(); }
    }

    public event Action? Changed;

    public RecentNotebooksService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CanfarDesktop", "Notebook");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "recent-notebooks.json");
        Load();
    }

    public void AddOrUpdate(string filePath)
    {
        lock (_lock)
        {
            _entries.RemoveAll(e => e.Path.Equals(filePath, StringComparison.OrdinalIgnoreCase));
            _entries.Insert(0, new RecentNotebookEntry
            {
                Path = filePath,
                Name = Path.GetFileName(filePath),
                OpenedAt = DateTime.UtcNow
            });
            if (_entries.Count > MaxRecent)
                _entries = _entries.Take(MaxRecent).ToList();
            Save();
        }
        Changed?.Invoke();
    }

    public void Remove(string filePath)
    {
        lock (_lock)
        {
            _entries.RemoveAll(e => e.Path.Equals(filePath, StringComparison.OrdinalIgnoreCase));
            Save();
        }
        Changed?.Invoke();
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
            Save();
        }
        Changed?.Invoke();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var json = File.ReadAllText(_filePath);
            _entries = JsonSerializer.Deserialize<List<RecentNotebookEntry>>(json) ?? [];
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Recent notebooks load failed: {ex.Message}");
            _entries = [];
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Recent notebooks save failed: {ex.Message}");
        }
    }
}

public class RecentNotebookEntry
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime OpenedAt { get; set; }
}
