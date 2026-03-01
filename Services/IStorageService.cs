using CanfarDesktop.Models;

namespace CanfarDesktop.Services;

public interface IStorageService
{
    Task<StorageQuota?> GetQuotaAsync(string username);
}
