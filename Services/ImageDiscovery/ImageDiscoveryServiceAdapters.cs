using CanfarDesktop.Models;

namespace CanfarDesktop.Services.ImageDiscovery;

/// <summary>Adapts the app's session service to the coordinator's headless-launcher facade.</summary>
public class HeadlessProbeAdapter : IHeadlessProbeLauncher
{
    private readonly ISessionService _sessions;

    public HeadlessProbeAdapter(ISessionService sessions) => _sessions = sessions;

    public Task<IReadOnlyList<string>> LaunchHeadlessAsync(SessionLaunchParams launchParams, CancellationToken cancellationToken)
        => _sessions.LaunchHeadlessAsync(launchParams, cancellationToken);

    public async Task<IReadOnlyList<HeadlessJobStatus>> GetHeadlessJobsAsync(CancellationToken cancellationToken)
    {
        var sessions = await _sessions.GetSessionsAsync();
        return sessions
            .Where(s => string.Equals(s.SessionType, "headless", StringComparison.OrdinalIgnoreCase))
            .Select(s => new HeadlessJobStatus(s.Id, s.Status, IsTerminal(s.Status), IsFailed(s.Status)))
            .ToList();
    }

    public async Task<string> GetLogsAsync(string id, CancellationToken cancellationToken)
        => await _sessions.GetSessionLogsAsync(id) ?? string.Empty;

    public async Task<string> GetEventsAsync(string id, CancellationToken cancellationToken)
        => await _sessions.GetSessionEventsAsync(id) ?? string.Empty;

    private static bool IsTerminal(string status)
        => status is "Succeeded" or "Completed" or "Failed" or "Error" or "Terminating";

    private static bool IsFailed(string status)
        => status is "Failed" or "Error";
}

/// <summary>Adapts the app's storage service to the coordinator's VOSpace-transfer facade.</summary>
public class VoSpaceFileTransferAdapter : IVoSpaceFileTransfer
{
    private readonly IStorageService _storage;

    public VoSpaceFileTransferAdapter(IStorageService storage) => _storage = storage;

    public async Task UploadFileAsync(string username, string remotePath, string localPath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(localPath);
        await _storage.UploadFileAsync($"{username}/{remotePath}", stream, "application/x-sh");
    }

    public async Task<string> DownloadToTempAsync(string username, string path, CancellationToken cancellationToken)
    {
        await using var stream = await _storage.DownloadFileAsync($"{username}/{path}");
        var temp = Path.GetTempFileName();
        await using (var fs = File.Create(temp))
            await stream.CopyToAsync(fs, cancellationToken);
        return temp;
    }

    public async Task EnsureFolderAsync(string username, string parentPath, string folderName, CancellationToken cancellationToken)
    {
        var remote = string.IsNullOrEmpty(parentPath) ? username : $"{username}/{parentPath}";
        try { await _storage.CreateFolderAsync(remote, folderName); }
        catch { /* idempotent — folder likely already exists */ }
    }
}
