using System.Text.Json;
using Windows.Storage;
using CanfarDesktop.Models;

namespace CanfarDesktop.Services;

public class RecentLaunchService : IRecentLaunchService
{
    private const int MaxEntries = 10;
    private const string FileName = "recent_launches.json";
    private readonly string? _filePath;
    private List<RecentLaunch> _launches = [];

    public RecentLaunchService()
    {
        try
        {
            var folder = ApplicationData.Current.LocalFolder.Path;
            _filePath = Path.Combine(folder, FileName);
            _launches = ReadFromDisk();
        }
        catch
        {
            // Running unpackaged — persist in-memory only
            _filePath = null;
        }
    }

    public void Save(RecentLaunch launch)
    {
        // Prepend new entry
        _launches.Insert(0, launch);

        // Cap at max entries
        if (_launches.Count > MaxEntries)
            _launches.RemoveRange(MaxEntries, _launches.Count - MaxEntries);

        WriteToDisk();
    }

    public void Remove(RecentLaunch launch)
    {
        _launches.RemoveAll(l =>
            l.Type == launch.Type && l.Image == launch.Image && l.LaunchedAt == launch.LaunchedAt);
        WriteToDisk();
    }

    public List<RecentLaunch> Load()
    {
        return _launches.OrderByDescending(l => l.LaunchedAt).ToList();
    }

    public void Clear()
    {
        _launches.Clear();
        WriteToDisk();
    }

    private List<RecentLaunch> ReadFromDisk()
    {
        if (_filePath is null || !File.Exists(_filePath))
            return [];

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<RecentLaunch>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void WriteToDisk()
    {
        if (_filePath is null) return;

        try
        {
            var json = JsonSerializer.Serialize(_launches, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // Silently fail — not critical
        }
    }
}
