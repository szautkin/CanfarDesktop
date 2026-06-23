using System.Text.Json;
using Windows.Storage;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;

namespace CanfarDesktop.Services;

public interface ISearchStoreService
{
    List<RecentSearch> LoadRecentSearches();
    void SaveRecentSearch(RecentSearch search);
    void SaveAllRecentSearches(IEnumerable<RecentSearch> searches);
    void ClearRecentSearches();

    List<SavedQuery> LoadSavedQueries();
    void SaveQuery(SavedQuery query);
    void DeleteQuery(string name);
}

public class SearchStoreService : ISearchStoreService
{
    private const int MaxRecentSearches = 20;
    private const int SchemaVersion = 1;
    private const string RecentFile = "recent_searches.json";
    private const string SavedFile = "saved_queries.json";

    private readonly string? _recentPath;
    private readonly string? _savedPath;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public SearchStoreService()
    {
        try
        {
            var folder = ApplicationData.Current.LocalFolder.Path;
            _recentPath = Path.Combine(folder, RecentFile);
            _savedPath = Path.Combine(folder, SavedFile);
        }
        catch
        {
            // Unpackaged — no local folder
        }
    }

    public List<RecentSearch> LoadRecentSearches()
        => DiskPersistence.Read(_recentPath, SchemaVersion, () => new List<RecentSearch>(), JsonOptions).Value;

    public void SaveRecentSearch(RecentSearch search)
    {
        var list = LoadRecentSearches();
        list.Insert(0, search);
        if (list.Count > MaxRecentSearches)
            list.RemoveRange(MaxRecentSearches, list.Count - MaxRecentSearches);
        DiskPersistence.Write(_recentPath, list, SchemaVersion, JsonOptions);
    }

    public void SaveAllRecentSearches(IEnumerable<RecentSearch> searches)
        => DiskPersistence.Write(_recentPath, searches.ToList(), SchemaVersion, JsonOptions);

    public void ClearRecentSearches()
    {
        if (_recentPath is not null && File.Exists(_recentPath))
            File.Delete(_recentPath);
    }

    public List<SavedQuery> LoadSavedQueries()
        => DiskPersistence.Read(_savedPath, SchemaVersion, () => new List<SavedQuery>(), JsonOptions).Value;

    public void SaveQuery(SavedQuery query)
    {
        var list = LoadSavedQueries();
        list.RemoveAll(q => q.Name == query.Name);
        list.Insert(0, query);
        DiskPersistence.Write(_savedPath, list, SchemaVersion, JsonOptions);
    }

    public void DeleteQuery(string name)
    {
        var list = LoadSavedQueries();
        list.RemoveAll(q => q.Name == name);
        DiskPersistence.Write(_savedPath, list, SchemaVersion, JsonOptions);
    }
}
