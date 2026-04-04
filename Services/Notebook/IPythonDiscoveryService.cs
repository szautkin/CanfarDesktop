namespace CanfarDesktop.Services.Notebook;

/// <summary>
/// Finds a usable Python installation on the system.
/// </summary>
public interface IPythonDiscoveryService
{
    string? PythonPath { get; }
    string? PythonVersion { get; }
    Task<string?> FindPythonAsync();
}
