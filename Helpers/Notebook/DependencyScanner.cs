namespace CanfarDesktop.Helpers.Notebook;

using System.Diagnostics;
using System.Text.RegularExpressions;
using CanfarDesktop.Models.Notebook;

/// <summary>
/// Scans notebook cells for import statements and checks which packages are missing.
/// </summary>
public static partial class DependencyScanner
{
    [GeneratedRegex(@"^\s*import\s+(\w+)", RegexOptions.Multiline)]
    private static partial Regex ImportRegex();

    [GeneratedRegex(@"^\s*from\s+(\w+)(?:\.\w+)*\s+import", RegexOptions.Multiline)]
    private static partial Regex FromImportRegex();

    // Map module names to pip package names where they differ
    private static readonly Dictionary<string, string> ModuleToPip = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PIL"] = "Pillow",
        ["cv2"] = "opencv-python",
        ["sklearn"] = "scikit-learn",
        ["yaml"] = "PyYAML",
        ["bs4"] = "beautifulsoup4",
        ["attr"] = "attrs",
        ["dateutil"] = "python-dateutil",
        ["astroquery"] = "astroquery",
        ["astroplan"] = "astroplan",
        ["mocpy"] = "mocpy",
        ["cdshealpix"] = "cdshealpix",
        ["ipyaladin"] = "ipyaladin",
        ["pyvo"] = "pyvo",
        ["lightkurve"] = "lightkurve",
    };

    // Standard library modules to skip
    private static readonly HashSet<string> StdLib = new(StringComparer.OrdinalIgnoreCase)
    {
        "os", "sys", "io", "re", "json", "math", "time", "datetime", "pathlib",
        "collections", "functools", "itertools", "typing", "dataclasses",
        "abc", "copy", "enum", "string", "textwrap", "struct", "codecs",
        "csv", "configparser", "argparse", "logging", "warnings", "traceback",
        "subprocess", "threading", "multiprocessing", "socket", "http",
        "urllib", "email", "html", "xml", "sqlite3", "hashlib", "hmac",
        "secrets", "tempfile", "shutil", "glob", "fnmatch", "stat",
        "gzip", "zipfile", "tarfile", "pickle", "shelve", "marshal",
        "random", "statistics", "decimal", "fractions", "operator",
        "contextlib", "inspect", "dis", "pdb", "unittest", "doctest",
        "pprint", "platform", "ctypes", "types", "weakref", "gc",
        "importlib", "pkgutil", "distutils", "venv", "site",
    };

    /// <summary>
    /// Extract unique third-party module names from all code cells.
    /// </summary>
    public static HashSet<string> ExtractImports(NotebookDocument document)
    {
        var modules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var cell in document.Cells)
        {
            if (cell.CellType != "code") continue;
            var source = cell.SourceText;

            foreach (Match m in ImportRegex().Matches(source))
                modules.Add(m.Groups[1].Value);
            foreach (Match m in FromImportRegex().Matches(source))
                modules.Add(m.Groups[1].Value);
        }

        // Remove stdlib
        modules.ExceptWith(StdLib);
        // Remove __future__ and other non-packages
        modules.RemoveWhere(m => m.StartsWith("__") || m.StartsWith("_"));

        return modules;
    }

    /// <summary>
    /// Check which modules are not installed. Returns list of (module, pipName) tuples.
    /// </summary>
    public static async Task<List<(string module, string pipName)>> FindMissingAsync(
        HashSet<string> modules, string pythonPath)
    {
        var missing = new List<(string, string)>();

        foreach (var mod in modules)
        {
            var installed = await IsInstalledAsync(mod, pythonPath);
            if (!installed)
            {
                var pipName = ModuleToPip.GetValueOrDefault(mod, mod);
                missing.Add((mod, pipName));
            }
        }

        return missing;
    }

    private static async Task<bool> IsInstalledAsync(string module, string pythonPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = $"-c \"import {module}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null) return false;
            await proc.WaitForExitAsync();
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Install packages via pip. Returns (stdout, stderr).
    /// </summary>
    public static async Task<(string output, string errors)> InstallAsync(
        IEnumerable<string> pipNames, string pythonPath)
    {
        var packages = string.Join(" ", pipNames);
        var psi = new ProcessStartInfo
        {
            FileName = pythonPath,
            Arguments = $"-m pip install {packages}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi);
        if (proc is null) return ("", "Failed to start pip");

        var stdout = await proc.StandardOutput.ReadToEndAsync();
        var stderr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        return (stdout, stderr);
    }
}
