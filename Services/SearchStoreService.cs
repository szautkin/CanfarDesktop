using System.Text.Json;
using Windows.Storage;
using CanfarDesktop.Models;

namespace CanfarDesktop.Services;

public interface ISearchStoreService
{
    List<RecentSearch> LoadRecentSearches();
    void SaveRecentSearch(RecentSearch search);
    void ClearRecentSearches();

    List<SavedQuery> LoadSavedQueries();
    void SaveQuery(SavedQuery query);
    void DeleteQuery(string name);
}

public class SearchStoreService : ISearchStoreService
{
    private const int MaxRecentSearches = 20;
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
    {
        if (_recentPath is null || !File.Exists(_recentPath)) return [];
        try
        {
            var json = File.ReadAllText(_recentPath);
            return JsonSerializer.Deserialize<List<RecentSearch>>(json, JsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load recent searches: {ex.Message}");
            return [];
        }
    }

    public void SaveRecentSearch(RecentSearch search)
    {
        if (_recentPath is null) return;
        var list = LoadRecentSearches();
        list.Insert(0, search);
        if (list.Count > MaxRecentSearches)
            list.RemoveRange(MaxRecentSearches, list.Count - MaxRecentSearches);

        File.WriteAllText(_recentPath, JsonSerializer.Serialize(list, JsonOptions));
    }

    public void ClearRecentSearches()
    {
        if (_recentPath is not null && File.Exists(_recentPath))
            File.Delete(_recentPath);
    }

    public List<SavedQuery> LoadSavedQueries()
    {
        if (_savedPath is null || !File.Exists(_savedPath)) return [];
        try
        {
            var json = File.ReadAllText(_savedPath);
            return JsonSerializer.Deserialize<List<SavedQuery>>(json, JsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load saved queries: {ex.Message}");
            return [];
        }
    }

    public void SaveQuery(SavedQuery query)
    {
        if (_savedPath is null) return;
        var list = LoadSavedQueries();
        list.RemoveAll(q => q.Name == query.Name);
        list.Insert(0, query);
        File.WriteAllText(_savedPath, JsonSerializer.Serialize(list, JsonOptions));
    }

    public void DeleteQuery(string name)
    {
        if (_savedPath is null) return;
        var list = LoadSavedQueries();
        list.RemoveAll(q => q.Name == name);
        File.WriteAllText(_savedPath, JsonSerializer.Serialize(list, JsonOptions));
    }
}
