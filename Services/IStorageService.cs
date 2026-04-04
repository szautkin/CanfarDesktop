using CanfarDesktop.Models;

namespace CanfarDesktop.Services;

public interface IStorageService
{
    Task<StorageQuota?> GetQuotaAsync(string username);
    Task<List<VoSpaceNode>> ListNodesAsync(string path, int? limit = null);
    Task UploadFileAsync(string remotePath, Stream content, string? contentType = null);
    Task<Stream> DownloadFileAsync(string remotePath);
    Task CreateFolderAsync(string remotePath, string folderName);
    Task DeleteNodeAsync(string remotePath);
}
