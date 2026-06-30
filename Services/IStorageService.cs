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

    /// <summary>
    /// Set a node's access-control list (VOSpace setNode). Each dimension is independent: pass
    /// <c>null</c> to leave it unchanged, an empty list to revoke all of that group dimension, or a
    /// (full GMS-URI) list to REPLACE it; <paramref name="isPublic"/> = false makes the node non-public.
    /// </summary>
    Task SetNodeAclAsync(string remotePath, IReadOnlyList<string>? groupRead, IReadOnlyList<string>? groupWrite, bool? isPublic, CancellationToken cancellationToken = default);
}
