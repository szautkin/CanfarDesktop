using CanfarDesktop.Models;

namespace CanfarDesktop.Services;

public interface IRecentLaunchService
{
    void Save(RecentLaunch launch);
    void Remove(RecentLaunch launch);
    List<RecentLaunch> Load();
    void Clear();
}
