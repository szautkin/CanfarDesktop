namespace CanfarDesktop.Services.Notebook;

using System.Diagnostics;

/// <summary>
/// Finds a usable Python 3.8+ installation on the system.
/// Search order: user-configured path > PATH > common Windows locations.
/// </summary>
public class PythonDiscoveryService : IPythonDiscoveryService
{
    private string? _cachedPath;
    private string? _cachedVersion;

    public string? PythonPath => _cachedPath;
    public string? PythonVersion => _cachedVersion;

    /// <summary>
    /// Find Python. Returns the executable path, or null if not found.
    /// Result is cached for the session.
    /// </summary>
    public async Task<string?> FindPythonAsync()
    {
        if (_cachedPath is not null) return _cachedPath;

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        // 1. Check real Python installations first (NOT the Windows Store stub)
        // The Store stub (WindowsApps\python.exe) is unreliable for subprocess I/O
        string[] preferredPaths =
        [
            Path.Combine(localAppData, @"Python\pythoncore-3.14-64\python.exe"),
            Path.Combine(localAppData, @"Python\pythoncore-3.13-64\python.exe"),
            Path.Combine(localAppData, @"Python\pythoncore-3.12-64\python.exe"),
            Path.Combine(localAppData, @"Programs\Python\Python314\python.exe"),
            Path.Combine(localAppData, @"Programs\Python\Python313\python.exe"),
            Path.Combine(localAppData, @"Programs\Python\Python312\python.exe"),
            Path.Combine(localAppData, @"Programs\Python\Python311\python.exe"),
        ];

        foreach (var path in preferredPaths)
        {
            if (File.Exists(path))
            {
                var result = await TryPythonAsync(path);
                if (result is not null) return Cache(result.Value);
            }
        }

        // 2. Check PATH (may return Store stub — filter it out)
        var pathResult = await TryPythonAsync("python");
        if (pathResult is not null && !pathResult.Value.path.Contains("WindowsApps"))
            return Cache(pathResult.Value);

        pathResult = await TryPythonAsync("python3");
        if (pathResult is not null && !pathResult.Value.path.Contains("WindowsApps"))
            return Cache(pathResult.Value);

        // 3. Other common Windows install locations
        string[] commonPaths =
        [
            Path.Combine(localAppData, @"Programs\Python\Python312\python.exe"),
            Path.Combine(localAppData, @"Programs\Python\Python311\python.exe"),
            Path.Combine(localAppData, @"Programs\Python\Python310\python.exe"),
            Path.Combine(localAppData, @"Programs\Python\Python39\python.exe"),
            Path.Combine(localAppData, @"Programs\Python\Python38\python.exe"),
            Path.Combine(programFiles, @"Python313\python.exe"),
            Path.Combine(programFiles, @"Python312\python.exe"),
            Path.Combine(programFiles, @"Python311\python.exe"),
            Path.Combine(programFiles, @"Python310\python.exe"),
            @"C:\Python313\python.exe",
            @"C:\Python312\python.exe",
            @"C:\Python311\python.exe",
            @"C:\Python310\python.exe",
        ];

        foreach (var path in commonPaths)
        {
            if (File.Exists(path))
            {
                var result = await TryPythonAsync(path);
                if (result is not null) return Cache(result.Value);
            }
        }

        // 4. Conda on PATH
        pathResult = await TryPythonAsync("conda");
        if (pathResult is not null)
        {
            var condaResult = await TryPythonAsync("conda run python");
            if (condaResult is not null) return Cache(condaResult.Value);
        }

        // 5. Last resort: Windows Store stub (unreliable but better than nothing)
        pathResult = await TryPythonAsync("python");
        if (pathResult is not null) return Cache(pathResult.Value);

        return null;
    }

    private string Cache((string path, string version) result)
    {
        _cachedPath = result.path;
        _cachedVersion = result.version;
        return result.path;
    }

    /// <summary>
    /// Try to run `python --version` and verify >= 3.8.
    /// Returns (resolvedPath, version) or null.
    /// </summary>
    private static async Task<(string path, string version)?> TryPythonAsync(string executable)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi);
            if (proc is null) return null;

            var output = await proc.StandardOutput.ReadToEndAsync();
            var error = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            // Python --version outputs to stdout (3.x) or stderr (2.x)
            var versionLine = !string.IsNullOrWhiteSpace(output) ? output.Trim() : error.Trim();
            if (!versionLine.StartsWith("Python ")) return null;

            var versionStr = versionLine["Python ".Length..];
            if (!Version.TryParse(versionStr, out var version)) return null;
            if (version.Major < 3 || (version.Major == 3 && version.Minor < 8)) return null;

            // Resolve the full path
            var resolvedPath = executable;
            if (!Path.IsPathRooted(executable))
            {
                // Try to resolve via `where` on Windows
                var wherePsi = new ProcessStartInfo
                {
                    FileName = "where",
                    Arguments = executable,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var whereProc = Process.Start(wherePsi);
                if (whereProc is not null)
                {
                    var whereLine = (await whereProc.StandardOutput.ReadLineAsync())?.Trim();
                    await whereProc.WaitForExitAsync();
                    if (!string.IsNullOrEmpty(whereLine) && File.Exists(whereLine))
                        resolvedPath = whereLine;
                }
            }

            return (resolvedPath, versionStr);
        }
        catch
        {
            return null;
        }
    }
}
