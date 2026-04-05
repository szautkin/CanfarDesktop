namespace CanfarDesktop.Services;

using CanfarDesktop.Models;

/// <summary>
/// Filesystem operations with FileSystemWatcher for live refresh.
/// Debounces change events to avoid flooding the UI.
/// </summary>
public class LocalFileService : ILocalFileService, IDisposable
{
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new();
    private readonly Dictionary<string, Timer> _debounceTimers = new();
    private readonly object _lock = new();

    public event Action<string>? DirectoryChanged;

    public List<LocalFileNode> GetChildren(string directoryPath, bool showHidden = false)
    {
        var nodes = new List<LocalFileNode>();
        if (!Directory.Exists(directoryPath)) return nodes;

        try
        {
            var dirInfo = new DirectoryInfo(directoryPath);

            // Folders first
            foreach (var dir in dirInfo.EnumerateDirectories().OrderBy(d => d.Name))
            {
                if (!showHidden && (dir.Attributes & FileAttributes.Hidden) != 0) continue;
                nodes.Add(new LocalFileNode
                {
                    Name = dir.Name,
                    FullPath = dir.FullName,
                    IsFolder = true,
                    DateModified = dir.LastWriteTime,
                    HasUnrealizedChildren = true,
                });
            }

            // Then files
            foreach (var file in dirInfo.EnumerateFiles().OrderBy(f => f.Name))
            {
                if (!showHidden && (file.Attributes & FileAttributes.Hidden) != 0) continue;
                nodes.Add(new LocalFileNode
                {
                    Name = file.Name,
                    FullPath = file.FullName,
                    IsFolder = false,
                    Extension = file.Extension,
                    DateModified = file.LastWriteTime,
                    SizeBytes = file.Length,
                });
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (DirectoryNotFoundException) { }

        return nodes;
    }

    public void CreateFolder(string parentPath, string name)
    {
        var path = Path.Combine(parentPath, name);
        Directory.CreateDirectory(path);
    }

    public void CreateFile(string parentPath, string name)
    {
        var path = Path.Combine(parentPath, name);
        File.WriteAllText(path, "");
    }

    public void Delete(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        else if (File.Exists(path))
            File.Delete(path);
    }

    public void Rename(string oldPath, string newName)
    {
        var dir = Path.GetDirectoryName(oldPath)!;
        var newPath = Path.Combine(dir, newName);
        if (Directory.Exists(oldPath))
            Directory.Move(oldPath, newPath);
        else if (File.Exists(oldPath))
            File.Move(oldPath, newPath);
    }

    public void Move(string sourcePath, string destFolderPath)
    {
        var name = Path.GetFileName(sourcePath);
        var dest = Path.Combine(destFolderPath, name);
        if (Directory.Exists(sourcePath))
            Directory.Move(sourcePath, dest);
        else if (File.Exists(sourcePath))
            File.Move(sourcePath, dest);
    }

    public void CopyToClipboard(string path)
    {
        // Will be called from UI thread where clipboard is available
    }

    public void WatchDirectory(string path)
    {
        lock (_lock)
        {
            if (_watchers.ContainsKey(path)) return;

            try
            {
                var watcher = new FileSystemWatcher(path)
                {
                    EnableRaisingEvents = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                    IncludeSubdirectories = false,
                };
                watcher.Created += (_, _) => DebouncedNotify(path);
                watcher.Deleted += (_, _) => DebouncedNotify(path);
                watcher.Renamed += (_, _) => DebouncedNotify(path);
                watcher.Changed += (_, _) => DebouncedNotify(path);
                _watchers[path] = watcher;
            }
            catch { /* can't watch this directory */ }
        }
    }

    public void UnwatchDirectory(string path)
    {
        lock (_lock)
        {
            if (_watchers.Remove(path, out var watcher))
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            if (_debounceTimers.Remove(path, out var timer))
                timer.Dispose();
        }
    }

    private void DebouncedNotify(string path)
    {
        lock (_lock)
        {
            if (_debounceTimers.TryGetValue(path, out var existing))
                existing.Dispose();

            _debounceTimers[path] = new Timer(_ =>
            {
                DirectoryChanged?.Invoke(path);
            }, null, 300, Timeout.Infinite);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var w in _watchers.Values) { w.EnableRaisingEvents = false; w.Dispose(); }
            foreach (var t in _debounceTimers.Values) t.Dispose();
            _watchers.Clear();
            _debounceTimers.Clear();
        }
        GC.SuppressFinalize(this);
    }
}
