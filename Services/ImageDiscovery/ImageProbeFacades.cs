using CanfarDesktop.Models;

namespace CanfarDesktop.Services.ImageDiscovery;

/// <summary>Minimal terminal-state view of a headless probe job for the coordinator's poll loop.</summary>
public record HeadlessJobStatus(string Id, string Status, bool IsTerminal, bool IsFailed);

/// <summary>The slice of the session/headless service the coordinator needs (mockable in tests).</summary>
public interface IHeadlessProbeLauncher
{
    Task<IReadOnlyList<string>> LaunchHeadlessAsync(SessionLaunchParams launchParams, CancellationToken cancellationToken);
    Task<IReadOnlyList<HeadlessJobStatus>> GetHeadlessJobsAsync(CancellationToken cancellationToken);
    Task<string> GetLogsAsync(string id, CancellationToken cancellationToken);
    Task<string> GetEventsAsync(string id, CancellationToken cancellationToken);
}

/// <summary>The VOSpace operations the coordinator needs (upload probe, download manifest, mkdir).</summary>
public interface IVoSpaceFileTransfer
{
    Task UploadFileAsync(string username, string remotePath, string localPath, CancellationToken cancellationToken);
    Task<string> DownloadToTempAsync(string username, string path, CancellationToken cancellationToken);
    Task EnsureFolderAsync(string username, string parentPath, string folderName, CancellationToken cancellationToken);
}

/// <summary>Supplies the probe / inspector script bodies + their content-hashed upload filenames.</summary>
public interface IProbeScriptProvider
{
    string HomeSubdirectory { get; }
    string ProbeBody { get; }
    string InspectorBody { get; }
    string ProbeUploadFileName { get; }
    string InspectorUploadFileName { get; }
}
