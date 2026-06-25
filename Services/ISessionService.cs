using CanfarDesktop.Models;

namespace CanfarDesktop.Services;

public interface ISessionService
{
    Task<List<Session>> GetSessionsAsync(CancellationToken cancellationToken = default);
    Task<Session?> GetSessionAsync(string id, CancellationToken cancellationToken = default);
    Task<string?> LaunchSessionAsync(SessionLaunchParams launchParams, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> LaunchHeadlessAsync(SessionLaunchParams launchParams, CancellationToken cancellationToken = default);
    Task<bool> DeleteSessionAsync(string id, CancellationToken cancellationToken = default);
    Task RenewSessionAsync(string id, CancellationToken cancellationToken = default);
    Task<string?> GetSessionEventsAsync(string id, CancellationToken cancellationToken = default);
    Task<string?> GetSessionLogsAsync(string id, CancellationToken cancellationToken = default);
}
