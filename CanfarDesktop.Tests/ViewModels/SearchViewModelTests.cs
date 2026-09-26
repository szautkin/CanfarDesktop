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

    // ── Queries written in the ADQL editor (QA D1, D1b, D4) ───────────────────

    /// <summary>A result with the given columns, one row of values named after them.</summary>
    private static SearchResults Columns(params string[] columns) => new()
    {
        Columns = [.. columns],
        Rows = [new SearchResultRow { Values = columns.ToDictionary(c => c, c => $"v:{c}") }],
    };

    [Fact]
    public async Task AQueryOfOnesOwn_ShowsEveryColumnItAskedFor()
    {
        // It showed none: none of them is one the form shows first, so all were hidden.
        var (vm, tap, _) = Make();
        tap.Answer.SetResult(Columns("observationID", "productID", "time_exposure"));

        await vm.ExecuteAdqlAsync("SELECT TOP 5 observationID, productID, time_exposure FROM caom2.Plane");

        Assert.All(vm.ResultColumns, c => Assert.True(c.Visible, c.Key));
    }

    [Fact]
    public async Task TheFormsOwnColumns_ShowTheUsualOnes()
    {
        var (vm, tap, _) = Make();
        tap.Answer.SetResult(Columns([.. CanfarDesktop.Helpers.ADQLBuilder.ColumnKeys, "\"My Extra\""]));

        await vm.ExecuteAdqlAsync("SELECT … the form's columns, and one more");

        bool Visible(string key) => vm.ResultColumns.Single(c => c.Key == key).Visible;
        Assert.True(Visible("collection"));
        Assert.True(Visible("ra(j20000)"));
        Assert.False(Visible("productid"));     // the form has it, and does not show it first
        Assert.False(Visible("proposaltitle"));
        Assert.True(Visible("myextra"));        // added by whoever wrote the query
    }

    [Fact]
    public async Task AQueryRunInTheEditor_IsARecentSearch_ThatGoesBackToTheEditor()
    {
        var (vm, tap, store) = Make();
        tap.Answer.SetResult(Rows(3));
        const string query = "SELECT TOP 3 obsID\nFROM caom2.Observation";

        await vm.ExecuteAdqlAsync(query);

        store.Received(1).SaveRecentSearch(Arg.Is<RecentSearch>(r =>
            r.Adql == query && r.FormState == null && r.ResultCount == 3 && r.Summary == "SELECT TOP 3 obsID FROM caom2.Observation"));

        // Loaded back, it is the editor's again, and the form is left as it was.
        vm.AdqlText = string.Empty;
        vm.LoadFromRecentSearch(RecentSearch.FromEditor(query, 3, DateTime.UtcNow));
        Assert.Equal(query, vm.AdqlText);
        Assert.Equal("G006.010.684+41.269", vm.ObservationId);
    }

    [Fact]
    public async Task AQueryThatFindsNothing_IsNotKept()
    {
        var (vm, tap, store) = Make();
        tap.Answer.SetResult(Rows(0));

        await vm.ExecuteAdqlAsync("SELECT TOP 3 obsID FROM caom2.Observation WHERE 1 = 0");

        store.DidNotReceive().SaveRecentSearch(Arg.Any<RecentSearch>());
    }

    [Fact]
    public void AnEditorSearchesSummary_IsItsQueryOnOneLine_AsFarAsFits()
    {
        var summary = RecentSearch.FromEditor("SELECT TOP 10\n    " + new string('x', 100), 1, DateTime.UtcNow).Summary;

        Assert.Equal(60, summary.Length);
        Assert.StartsWith("SELECT TOP 10 xxx", summary);
        Assert.EndsWith("…", summary);
    }

    [Fact]
    public async Task Exports_NameColumnsAsTheGridDoes()
    {
        // The header said "RA (J2000.0)" with its quotes in it, where the grid says RA (J2000.0).
        var (vm, tap, _) = Make();
        tap.Answer.SetResult(Columns("collection", "\"RA (J2000.0)\"", "\"Target Name, as given\""));
        await vm.ExecuteAdqlAsync("SELECT …");

        Assert.Equal("collection,RA (J2000.0),\"Target Name, as given\"", vm.ExportResultsCsv().Split(Environment.NewLine)[0]);
        Assert.Equal("collection\tRA (J2000.0)\tTarget Name, as given", vm.ExportResultsTsv().Split(Environment.NewLine)[0]);
        Assert.Equal("RA (J2000.0)", vm.ResultColumns.Single(c => c.Key == "ra(j20000)").Label);
    }
}
