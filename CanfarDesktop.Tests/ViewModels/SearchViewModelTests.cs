using NSubstitute;
using Xunit;
using CanfarDesktop.Models;
using CanfarDesktop.Services;
using CanfarDesktop.ViewModels;

namespace CanfarDesktop.Tests.ViewModels;

/// <summary>
/// A search the person can stop. A query CADC takes minutes over used to hold the page — Search greyed,
/// a spinner, no way out; Cancel now stops it, and what was on screen before stays.
/// </summary>
public class SearchViewModelTests
{
    /// <summary>A TAP service whose queries answer only when told to, or fail, and which notes being cancelled.</summary>
    private sealed class Tap : ITAPService
    {
        public TaskCompletionSource<SearchResults> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationToken _token;
        public bool Cancelled => _token.IsCancellationRequested;
        public List<string> Queries { get; } = [];

        public Task<SearchResults> ExecuteQueryAsync(string adql, int maxRecords = 10000, CancellationToken cancellationToken = default)
        {
            Queries.Add(adql);
            _token = cancellationToken;
            return Answer.Task.WaitAsync(cancellationToken);
        }

        public Task<List<DataTrainRow>> GetDataTrainAsync() => Task.FromResult(new List<DataTrainRow>());
        public Task<ResolverResult?> ResolveTargetAsync(string target, string service = "ALL", CancellationToken cancellationToken = default)
            => Task.FromResult<ResolverResult?>(null);
    }

    private static SearchResults Rows(int count) => new()
    {
        Columns = ["obsID"],
        Rows = Enumerable.Range(0, count).Select(i => new SearchResultRow { Values = new() { ["obsID"] = $"o{i}" } }).ToList(),
    };

    private static (SearchViewModel Vm, Tap Tap, ISearchStoreService Store) Make()
    {
        var tap = new Tap();
        var store = Substitute.For<ISearchStoreService>();
        store.LoadRecentSearches().Returns([]);
        return (new SearchViewModel(tap, store, new InMemoryColumnUnitStore()) { ObservationId = "G006.010.684+41.269" }, tap, store);
    }

    [Fact]
    public async Task ACancelledSearch_StopsItsQuery_AndLeavesWhatWasShown()
    {
        var (vm, tap, store) = Make();

        var running = vm.SearchAsync();
        Assert.True(vm.IsSearching);
        vm.CancelSearch();
        await running;

        Assert.True(tap.Cancelled);
        Assert.False(vm.IsSearching);
        Assert.True(vm.SearchCancelled);
        Assert.False(vm.HasError);          // cancelled is not failed
        Assert.Null(vm.Results);            // nothing new was shown
        store.DidNotReceive().SaveRecentSearch(Arg.Any<RecentSearch>());
    }

    /// <summary>After a cancel, the next search runs as usual, is not taken for cancelled, and is kept as recent.</summary>
    [Fact]
    public async Task AfterACancel_TheNextSearchRunsAsUsual()
    {
        var (vm, tap, store) = Make();
        var first = vm.SearchAsync();
        vm.CancelSearch();
        await first;

        tap.Answer.SetResult(Rows(2));
        await vm.SearchAsync();

        Assert.False(vm.SearchCancelled);
        Assert.Equal(2, vm.Results!.TotalRows);
        Assert.Equal(2, tap.Queries.Count);
        store.Received(1).SaveRecentSearch(Arg.Any<RecentSearch>());
    }

    /// <summary>A query CADC refuses says why, and is not mistaken for one the person cancelled.</summary>
    [Fact]
    public async Task ARefusedQuery_SaysWhy()
    {
        var (vm, tap, _) = Make();
        tap.Answer.SetException(new HttpRequestException("TAP query failed (400): Function [GETDATE] is not found in TapSchema"));

        await vm.SearchAsync();

        Assert.True(vm.HasError);
        Assert.False(vm.SearchCancelled);
        Assert.Contains("GETDATE", vm.ErrorMessage);
    }

    /// <summary>Cancel with nothing running does nothing.</summary>
    [Fact]
    public void CancellingWhenNothingRuns_DoesNothing()
    {
        var (vm, _, _) = Make();

        vm.CancelSearch();

        Assert.False(vm.IsSearching);
        Assert.False(vm.SearchCancelled);
    }
}
