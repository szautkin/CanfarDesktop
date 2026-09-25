using Xunit;
using CanfarDesktop.Models.AICompute;
using CanfarDesktop.Services.AICompute;

namespace CanfarDesktop.Tests.Services;

/// <summary>
/// The history the Remote Compute screen shows: every run sent to the compute session, who sent it,
/// and what came of it.
/// </summary>
public sealed class ComputeRunStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"verbinal_runs_{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    private static ComputeRun Run(string id, ComputeRunAuthor author = ComputeRunAuthor.Agent)
        => new(id, author, "python", $"print('{id}')", 60, "2026-09-24T12:00:00Z");

    private static RunCodeResult Result(string status, int exit = 0)
        => new(status, exit, "out", null, "", null, 1200, false, "2026-09-24T12:00:01Z", "2026-09-24T12:00:02Z");

    [Fact]
    public void RunsAreListedNewestFirst()
    {
        var store = new ComputeRunStore(_path);
        store.Add(Run("a"));
        store.Add(Run("b"));

        Assert.Equal(["b", "a"], store.All().Select(r => r.Id));
    }

    [Fact]
    public void AFreshRunIsStillOut()
    {
        var store = new ComputeRunStore(_path);
        store.Add(Run("a"));

        Assert.False(store.Find("a")!.IsFinished);
    }

    /// <summary>A result records the watcher's verdict, and the run keeps its place: the list is by when it was sent.</summary>
    [Fact]
    public void AResultFinishesTheRunInPlace()
    {
        var store = new ComputeRunStore(_path);
        store.Add(Run("a"));
        store.Add(Run("b"));

        store.Complete("a", Result("ok"));

        var a = store.Find("a")!;
        Assert.Equal("ok", a.Status);
        Assert.Equal(0, a.ExitCode);
        Assert.Equal(1200, a.DurationMs);
        Assert.Equal(["b", "a"], store.All().Select(r => r.Id));
    }

    [Fact]
    public void AStatusIsReadTheWayTheWatcherMeantIt()
    {
        var store = new ComputeRunStore(_path);
        store.Add(Run("a"));
        store.Add(Run("b"));

        store.Complete("a", Result(" Error ", 1));
        store.Complete("b", Result(""));

        Assert.Equal("error", store.Find("a")!.Status);
        Assert.Equal("error", store.Find("b")!.Status);   // a result with no verdict did not succeed
    }

    [Fact]
    public void AResultForARunNobodyRecordedChangesNothing()
    {
        var store = new ComputeRunStore(_path);
        var raised = 0;
        store.Changed += () => raised++;

        store.Complete("unknown", Result("ok"));

        Assert.Empty(store.All());
        Assert.Equal(0, raised);
    }

    /// <summary>Giving up on a run must not overwrite a result that arrived first.</summary>
    [Fact]
    public void ClosingAFinishedRunKeepsItsResult()
    {
        var store = new ComputeRunStore(_path);
        store.Add(Run("a"));
        store.Complete("a", Result("ok"));

        store.Close("a", ComputeRun.NoResult);

        Assert.Equal("ok", store.Find("a")!.Status);
    }

    /// <summary>
    /// Every poll after a run finishes reads the same result again. Recording it again would rewrite the
    /// file and redraw the screen's list each time, for nothing.
    /// </summary>
    [Fact]
    public void TheSameResultTwiceChangesNothing()
    {
        var store = new ComputeRunStore(_path);
        store.Add(Run("a"));
        store.Complete("a", Result("ok"));
        var written = File.GetLastWriteTimeUtc(_path);
        var raised = 0;
        store.Changed += () => raised++;

        store.Complete("a", Result("ok"));

        Assert.Equal(0, raised);
        Assert.Equal(written, File.GetLastWriteTimeUtc(_path));
    }

    [Fact]
    public void ARunThatNeverCameBackIsClosed()
    {
        var store = new ComputeRunStore(_path);
        store.Add(Run("a"));

        store.Close("a", ComputeRun.NoResult);

        Assert.Equal(ComputeRun.NoResult, store.Find("a")!.Status);
        Assert.True(store.Find("a")!.IsFinished);
    }

    [Fact]
    public void WhoSentARunIsRemembered()
    {
        new ComputeRunStore(_path).Add(Run("a", ComputeRunAuthor.User));

        Assert.Equal(ComputeRunAuthor.User, new ComputeRunStore(_path).Find("a")!.Author);
    }

    [Fact]
    public void TheHistoryIsCapped()
    {
        var store = new ComputeRunStore(_path);
        for (var i = 0; i < ComputeRunStore.MaxRuns + 5; i++) store.Add(Run($"r{i}"));

        var all = store.All();
        Assert.Equal(ComputeRunStore.MaxRuns, all.Count);
        Assert.Equal($"r{ComputeRunStore.MaxRuns + 4}", all[0].Id);   // the newest survive
    }

    [Fact]
    public void ChangesAreAnnounced()
    {
        var store = new ComputeRunStore(_path);
        var raised = 0;
        store.Changed += () => raised++;

        store.Add(Run("a"));
        store.Complete("a", Result("ok"));
        store.Clear();

        Assert.Equal(3, raised);
        Assert.Empty(store.All());
    }
}
