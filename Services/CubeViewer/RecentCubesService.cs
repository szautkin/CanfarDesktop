using System.Text.Json;

namespace CanfarDesktop.Services.CubeViewer;

/// <summary>
/// Persists the list of recently opened FITS cubes to %LocalAppData% (the Windows analogue of the
/// macOS <c>CubeRecents</c> UserDefaults store; mirrors <c>RecentNotebooksService</c>). Capped at 8;
/// entries whose file no longer exists are dropped at load. Thread-safe via lock.
/// </summary>
public class RecentCubesService
{
    private const int MaxRecent = 8;
    private readonly string _filePath;
    private readonly object _lock = new();
    private List<RecentCubeEntry> _entries = [];

    public IReadOnlyList<RecentCubeEntry> Entries
    {
        get { lock (_lock) return _entries.AsReadOnly(); }
    }

    public event Action? Changed;

    /// <summary>Create the store. <paramref name="filePath"/> overrides the default location (for tests).</summary>
    public RecentCubesService(string? filePath = null)
    {
        if (filePath is null)
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CanfarDesktop", "CubeViewer");
            Directory.CreateDirectory(dir);
            filePath = Path.Combine(dir, "recent-cubes.json");
        }
        _filePath = filePath;
        Load();
    }

    /// <summary>Record an opened cube (moves an existing path to the top). Call on every successful load.</summary>
    public void AddOrUpdate(string filePath, string? displayName = null)
    {
        lock (_lock)
        {
            _entries.RemoveAll(e => e.Path.Equals(filePath, StringComparison.OrdinalIgnoreCase));
            _entries.Insert(0, new RecentCubeEntry
            {
                Path = filePath,
                Name = string.IsNullOrWhiteSpace(displayName) ? Path.GetFileName(filePath) : displayName,
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
            var loaded = JsonSerializer.Deserialize<List<RecentCubeEntry>>(json) ?? [];
            // Files deleted/moved since the last session would just produce dead entries — drop them.
            _entries = loaded.Where(e => !string.IsNullOrEmpty(e.Path) && File.Exists(e.Path)).ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Recent cubes load failed: {ex.Message}");
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
            System.Diagnostics.Debug.WriteLine($"Recent cubes save failed: {ex.Message}");
        }
    }
}

/// <summary>One recently opened cube: full path, display name (OBJECT or file name), last-opened time.</summary>
public class RecentCubeEntry
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime OpenedAt { get; set; }
}
