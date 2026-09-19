using System.Text.Json;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Services;

/// <summary>One recently opened file: full path, display name, last-opened time.</summary>
public sealed class RecentFileEntry
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime OpenedAt { get; set; }
}

/// <summary>
/// The list of files a viewer has opened before, newest first, on disk.
///
/// <para>This was written twice — <c>RecentCubesService</c> and <c>RecentNotebooksService</c>, the
/// former's own comment noting that it "mirrors RecentNotebooksService" — and the FITS viewer, which
/// had none, would have made it three. They differed in the cap, the folder, and two things that were
/// not deliberate: only one could be pointed at a temp path, so only one was testable, and only one
/// dropped entries whose file had since been deleted.</para>
///
/// <para>What varies between viewers is the folder, the cap and whether a missing file should be
/// dropped; all three are arguments. Thread-safe.</para>
/// </summary>
public class RecentFilesStore
{
    private readonly string _filePath;
    private readonly int _max;
    private readonly bool _pruneMissing;
    private readonly object _lock = new();
    private List<RecentFileEntry> _entries = [];

    /// <summary>Newest first.</summary>
    public IReadOnlyList<RecentFileEntry> Entries
    {
        get { lock (_lock) return _entries.AsReadOnly(); }
    }

    public event Action? Changed;

    /// <summary>
    /// A store under <c>%LocalAppData%\CanfarDesktop\{area}\{fileName}</c>.
    ///
    /// <paramref name="pruneMissing"/> drops entries whose file is gone when the list is LOADED. The
    /// cube and notebook lists want that — a dead row in an empty state is a dead end. A caller that
    /// would rather show the row and say it is missing passes false.
    /// </summary>
    public RecentFilesStore(string area, string fileName, int max, bool pruneMissing = true, string? filePath = null)
    {
        _max = max < 1 ? 1 : max;
        _pruneMissing = pruneMissing;

        if (filePath is null)
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CanfarDesktop", area);
            Directory.CreateDirectory(dir);
            filePath = Path.Combine(dir, fileName);
        }
        _filePath = filePath;
        Load();
    }

    /// <summary>Record an opened file, moving an existing path to the top. Call on every successful load.</summary>
    public void AddOrUpdate(string filePath, string? displayName = null)
    {
        lock (_lock)
        {
            _entries.RemoveAll(e => e.Path.Equals(filePath, StringComparison.OrdinalIgnoreCase));
            _entries.Insert(0, new RecentFileEntry
            {
                Path = filePath,
                Name = string.IsNullOrWhiteSpace(displayName) ? Path.GetFileName(filePath) : displayName,
                OpenedAt = DateTime.UtcNow,
            });
            if (_entries.Count > _max) _entries = _entries.Take(_max).ToList();
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
            var loaded = JsonSerializer.Deserialize<List<RecentFileEntry>>(File.ReadAllText(_filePath)) ?? [];
            _entries = loaded
                .Where(e => !string.IsNullOrEmpty(e.Path) && (!_pruneMissing || File.Exists(e.Path)))
                .Take(_max)
                .ToList();
        }
        catch (Exception ex)
        {
            // An unreadable list is a list, not a reason not to start.
            System.Diagnostics.Debug.WriteLine($"Recent files load failed ({_filePath}): {ex.Message}");
            _entries = [];
        }
    }

    private void Save()
    {
        try
        {
            AtomicFile.WriteAllText(_filePath,
                JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Recent files save failed ({_filePath}): {ex.Message}");
        }
    }
}
