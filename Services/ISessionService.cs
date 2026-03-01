using CanfarDesktop.Models;

namespace CanfarDesktop.Services;

public interface ISessionService
{
    Task<List<Session>> GetSessionsAsync();
    Task<Session?> GetSessionAsync(string id);
    Task<string?> LaunchSessionAsync(SessionLaunchParams launchParams);
    Task<bool> DeleteSessionAsync(string id);
    Task<bool> RenewSessionAsync(string id);
    Task<string?> GetSessionEventsAsync(string id);
    Task<string?> GetSessionLogsAsync(string id);
}
