using CanfarDesktop.Models;

namespace CanfarDesktop.Services;

public interface IPlatformService
{
    Task<SkahaStatsResponse?> GetStatsAsync(CancellationToken cancellationToken = default);
}
