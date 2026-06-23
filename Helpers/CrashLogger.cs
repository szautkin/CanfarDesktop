namespace CanfarDesktop.Helpers;

/// <summary>
/// Last-resort crash/diagnostics logging. Wires the process-wide unhandled-exception
/// sources and appends token-scrubbed entries to a local log file. No remote telemetry —
/// this preserves the app's no-data-collection stance; Store crash analytics are
/// provided natively by Partner Center.
/// </summary>
public static class CrashLogger
{
    private static readonly object _gate = new();

    /// <summary>Wire the non-UI unhandled-exception sources. Call once at startup.</summary>
    public static void Initialize()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log("AppDomain.UnhandledException", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>Append a scrubbed diagnostic entry. Never throws.</summary>
    public static void Log(string source, Exception? ex)
    {
        try
        {
            var entry = LogScrubber.Scrub($"[{DateTimeOffset.UtcNow:O}] {source}{Environment.NewLine}{ex}");
            System.Diagnostics.Debug.WriteLine(entry);

            var dir = ResolveLogDirectory();
            if (dir is null) return;

            var path = System.IO.Path.Combine(dir, "crash.log");
            lock (_gate)
            {
                System.IO.File.AppendAllText(
                    path, entry + Environment.NewLine + new string('-', 60) + Environment.NewLine);
            }
        }
        catch
        {
            // A crash handler must never throw.
        }
    }

    /// <summary>
    /// Append a scrubbed informational trace line (no exception). Mirrors to the debugger Output
    /// window and the local crash.log. Used to trace flows like image discovery. Never throws.
    /// </summary>
    public static void Info(string message)
    {
        try
        {
            var entry = LogScrubber.Scrub($"[{DateTimeOffset.UtcNow:O}] {message}");
            System.Diagnostics.Debug.WriteLine(entry);

            var dir = ResolveLogDirectory();
            if (dir is null) return;

            var path = System.IO.Path.Combine(dir, "crash.log");
            lock (_gate)
            {
                System.IO.File.AppendAllText(path, entry + Environment.NewLine);
            }
        }
        catch
        {
            // Diagnostics must never throw.
        }
    }

    private static string? ResolveLogDirectory()
    {
        try
        {
            return Windows.Storage.ApplicationData.Current.LocalFolder.Path;
        }
        catch
        {
            // Unpackaged (e.g. tests) — fall back to a temp folder.
            try
            {
                var p = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Verbinal");
                System.IO.Directory.CreateDirectory(p);
                return p;
            }
            catch
            {
                return null;
            }
        }
    }
}
