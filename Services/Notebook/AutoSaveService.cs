namespace CanfarDesktop.Services.Notebook;

using System.Diagnostics;
using CanfarDesktop.Helpers.Notebook;
using CanfarDesktop.Models.Notebook;

/// <summary>
/// Writes autosave checkpoints to %LocalAppData%/CanfarDesktop/AutoSave/.
/// File is deleted on clean close. Orphaned files detected by RecoveryService.
/// </summary>
public class AutoSaveService : IAutoSaveService
{
    private Timer? _timer;
    private Func<NotebookDocument>? _documentProvider;
    private Func<bool>? _isDirtyCheck;
    private string? _autoSavePath;
    private bool _disposed;
    private readonly object _lock = new();

    public string? AutoSavePath => _autoSavePath;
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);

    public void Start(string? originalPath, Func<NotebookDocument> documentProvider, Func<bool>? isDirtyCheck = null)
    {
        ArgumentNullException.ThrowIfNull(documentProvider);

        lock (_lock)
        {
            _documentProvider = documentProvider;
            _isDirtyCheck = isDirtyCheck;
            _autoSavePath = BuildAutoSavePath(originalPath);

            var dir = Path.GetDirectoryName(_autoSavePath);
            if (dir is not null) Directory.CreateDirectory(dir);

            _timer?.Dispose();
            _timer = new Timer(OnTimerTick, null, Interval, Interval);
        }
    }

    public void StopAndCleanup()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;

            if (_autoSavePath is not null && File.Exists(_autoSavePath))
            {
                try { File.Delete(_autoSavePath); }
                catch (Exception ex) { Debug.WriteLine($"AutoSave cleanup failed: {ex.Message}"); }
            }

            _autoSavePath = null;
            _documentProvider = null;
        }
    }

    public async Task<string?> SaveNowAsync()
    {
        return await Task.Run(() => WriteAutoSave());
    }

    private void OnTimerTick(object? state)
    {
        WriteAutoSave();
    }

    private string? WriteAutoSave()
    {
        Func<NotebookDocument>? provider;
        string? path;

        lock (_lock)
        {
            provider = _documentProvider;
            path = _autoSavePath;
        }

        if (provider is null || path is null) return null;

        // Skip autosave when document is not dirty
        Func<bool>? dirtyCheck;
        lock (_lock) { dirtyCheck = _isDirtyCheck; }
        if (dirtyCheck is not null && !dirtyCheck()) return null;

        try
        {
            var document = provider();
            var json = NotebookParser.Serialize(document);

            // Atomic write: tmp + rename prevents reading half-written files
            var tmpPath = path + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, path, overwrite: true);

            Debug.WriteLine($"AutoSave written: {path}");
            return path;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AutoSave failed: {ex.Message}");
            return null;
        }
    }

    private static string BuildAutoSavePath(string? originalPath)
    {
        var dir = GetAutoSaveDirectory();
        if (originalPath is not null)
        {
            var name = Path.GetFileNameWithoutExtension(originalPath);
            // Include a hash of the full path to avoid collisions for same-name files in different dirs
            var hash = originalPath.GetHashCode().ToString("x8");
            return Path.Combine(dir, $"{name}-{hash}.autosave.ipynb");
        }
        return Path.Combine(dir, $"untitled-{Guid.NewGuid():N}.autosave.ipynb");
    }

    internal static string GetAutoSaveDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "CanfarDesktop", "AutoSave");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopAndCleanup();
        GC.SuppressFinalize(this);
    }
}
