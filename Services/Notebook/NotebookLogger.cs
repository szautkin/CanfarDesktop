namespace CanfarDesktop.Services.Notebook;

using System.Diagnostics;

/// <summary>
/// Simple file logger for notebook operations. Writes to %LocalAppData%/CanfarDesktop/Logs/.
/// Not a full ILogger implementation — just a static helper for diagnostic logging.
/// Keeps last 7 days of logs, deletes older.
/// </summary>
public static class NotebookLogger
{
    private static readonly string LogDir;
    private static readonly string LogPath;
    private static readonly object Lock = new();

    static NotebookLogger()
    {
        LogDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CanfarDesktop", "Logs");
        Directory.CreateDirectory(LogDir);
        LogPath = Path.Combine(LogDir, $"jupiter-{DateTime.Now:yyyy-MM-dd}.log");

        // Clean old logs on startup
        CleanOldLogs(7);
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null)
    {
        Write("ERROR", ex is not null ? $"{message}: {ex.Message}" : message);
    }

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}";
        Debug.WriteLine(line);

        lock (Lock)
        {
            try { File.AppendAllText(LogPath, line + Environment.NewLine); }
            catch { /* best effort */ }
        }
    }

    private static void CleanOldLogs(int keepDays)
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-keepDays);
            foreach (var file in Directory.EnumerateFiles(LogDir, "jupiter-*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                    File.Delete(file);
            }
        }
        catch { /* best effort */ }
    }

    /// <summary>Open the log folder in File Explorer.</summary>
    public static void OpenLogFolder()
    {
        try { Process.Start(new ProcessStartInfo { FileName = LogDir, UseShellExecute = true }); }
        catch { /* best effort */ }
    }
}
