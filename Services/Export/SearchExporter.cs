namespace CanfarDesktop.Services.Export;

/// <summary>
/// Exports saved ADQL queries + recent searches (JSON + markdown with fenced sql blocks). Thin
/// adapter over <see cref="ISearchStoreService"/>; rendering lives in <see cref="SearchExportBuilder"/>.
/// </summary>
public class SearchExporter : IExportableModule
{
    private readonly ISearchStoreService _store;

    public SearchExporter(ISearchStoreService store) => _store = store;

    public string ModuleId => "search";
    public string DisplayName => "Search";

    public Task<ExportModuleOutput> ExportAsync(ExportOptions options)
        => Task.FromResult(SearchExportBuilder.Build(_store.LoadSavedQueries(), _store.LoadRecentSearches(), options, DateTimeOffset.UtcNow));
}
