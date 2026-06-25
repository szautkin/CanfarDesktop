using CanfarDesktop.Models;

namespace CanfarDesktop.Services;

public interface IStorageService
{
    Task<StorageQuota?> GetQuotaAsync(string username, CancellationToken cancellationToken = default);
    Task<List<VoSpaceNode>> ListNodesAsync(string path, int? limit = null, CancellationToken cancellationToken = default);
    Task UploadFileAsync(string remotePath, Stream content, string? contentType = null, CancellationToken cancellationToken = default);
    Task<Stream> DownloadFileAsync(string remotePath, CancellationToken cancellationToken = default);
    Task CreateFolderAsync(string remotePath, string folderName, CancellationToken cancellationToken = default);
    Task DeleteNodeAsync(string remotePath, CancellationToken cancellationToken = default);
}
