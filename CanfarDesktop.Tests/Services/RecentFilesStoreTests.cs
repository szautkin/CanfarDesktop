using Xunit;
using CanfarDesktop.Services;
using CanfarDesktop.Services.CubeViewer;
using CanfarDesktop.Services.Fits;
using CanfarDesktop.Services.Notebook;

namespace CanfarDesktop.Tests.Services;

/// <summary>
/// The recents list was written twice — the cube viewer's and the notebook's, the former's comment
/// noting it "mirrors RecentNotebooksService" — and the FITS viewer, which had none, would have made
/// three. What actually varies between them is a folder and a cap.
/// </summary>
public class RecentFilesStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "verbinal-recents-" + Guid.NewGuid().ToString("N")[..8]);

    public RecentFilesStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private string Store(string name = "recents.json") => Path.Combine(_dir, name);

    /// <summary>A real file on disk, so the prune-missing behaviour can be tested honestly.</summary>
    private string RealFile(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "x");
        return path;
    }

    private RecentFilesStore New(int max = 8, bool pruneMissing = true, string store = "recents.json")
        => new("TestArea", "unused.json", max, pruneMissing, Store(store));

    // ── Newest first ─────────────────────────────────────────────────────────

    [Fact]
    public void AddOrUpdate_PutsTheNewestFirst()
    {
        var s = New();
        s.AddOrUpdate(@"C:\a.fits");
        s.AddOrUpdate(@"C:\b.fits");

        Assert.Equal([@"C:\b.fits", @"C:\a.fits"], s.Entries.Select(e => e.Path).ToArray());
    }

    /// <summary>Re-opening a file moves it up rather than listing it twice.</summary>
    [Fact]
    public void AddOrUpdate_MovesAnExistingPathToTheTop()
    {
        var s = New();
        s.AddOrUpdate(@"C:\a.fits");
        s.AddOrUpdate(@"C:\b.fits");
        s.AddOrUpdate(@"C:\a.fits");

        Assert.Equal([@"C:\a.fits", @"C:\b.fits"], s.Entries.Select(e => e.Path).ToArray());
    }

    /// <summary>Windows paths differ in case and still name one file.</summary>
    [Fact]
    public void AddOrUpdate_MatchesAPathWhateverItsCase()
    {
        var s = New();
        s.AddOrUpdate(@"C:\Data\M31.fits");
        s.AddOrUpdate(@"c:\data\m31.FITS");

        Assert.Single(s.Entries);
    }

    [Fact]
    public void AddOrUpdate_FallsBackToTheFileNameWhenNoDisplayNameIsGiven()
    {
        var s = New();
        s.AddOrUpdate(@"C:\data\m31.fits");
        Assert.Equal("m31.fits", s.Entries[0].Name);

        s.AddOrUpdate(@"C:\data\m51.fits", "M51");
        Assert.Equal("M51", s.Entries[0].Name);
    }

    // ── The cap ──────────────────────────────────────────────────────────────

    [Fact]
    public void TheOldestFallsOffTheEndAtTheCap()
    {
        var s = New(max: 3);
        foreach (var n in new[] { "a", "b", "c", "d" }) s.AddOrUpdate($@"C:\{n}.fits");

        Assert.Equal([@"C:\d.fits", @"C:\c.fits", @"C:\b.fits"], s.Entries.Select(e => e.Path).ToArray());
    }

    /// <summary>A nonsense cap keeps one entry rather than none — a list of zero is not a list.</summary>
    [Fact]
    public void ACapBelowOneIsTreatedAsOne()
    {
        var s = New(max: 0);
        s.AddOrUpdate(@"C:\a.fits");

        Assert.Single(s.Entries);
    }

    // ── Across a restart ─────────────────────────────────────────────────────

    [Fact]
    public void TheListSurvivesARestart()
    {
        var first = New();
        first.AddOrUpdate(RealFile("kept.fits"), "Kept");

        var second = New();

        Assert.Single(second.Entries);
        Assert.Equal("Kept", second.Entries[0].Name);
    }

    /// <summary>
    /// A file deleted since it was recorded is a row that can only fail to open, so it is dropped when
    /// the list is loaded. Only one of the two originals did this.
    /// </summary>
    [Fact]
    public void AnEntryWhoseFileIsGoneIsDroppedAtLoad()
    {
        var kept = RealFile("still-here.fits");
        var doomed = RealFile("deleted.fits");

        var first = New();
        first.AddOrUpdate(kept);
        first.AddOrUpdate(doomed);
        File.Delete(doomed);

        Assert.Equal([kept], New().Entries.Select(e => e.Path).ToArray());
    }

    /// <summary>A caller that would rather show the row and say it is missing opts out.</summary>
    [Fact]
    public void PruningCanBeTurnedOff()
    {
        var first = New(pruneMissing: false);
        first.AddOrUpdate(@"C:\never-existed.fits");

        Assert.Single(New(pruneMissing: false).Entries);
    }

    /// <summary>An unreadable list is a list, not a reason the viewer cannot start.</summary>
    [Fact]
    public void ACorruptFileLoadsAsEmptyRatherThanThrowing()
    {
        File.WriteAllText(Store(), "{ not json");

        Assert.Empty(New().Entries);
    }

    // ── Remove and clear ─────────────────────────────────────────────────────

    [Fact]
    public void RemoveTakesOneEntryOut()
    {
        var s = New();
        s.AddOrUpdate(@"C:\a.fits");
        s.AddOrUpdate(@"C:\b.fits");
        s.Remove(@"C:\a.fits");

        Assert.Equal([@"C:\b.fits"], s.Entries.Select(e => e.Path).ToArray());
    }

    [Fact]
    public void ClearEmptiesTheList()
    {
        var s = New();
        s.AddOrUpdate(@"C:\a.fits");
        s.Clear();

        Assert.Empty(s.Entries);
        Assert.Empty(New().Entries);   // and it stays empty across a restart
    }

    [Fact]
    public void ChangedIsRaisedOnEveryMutation()
    {
        var s = New();
        var raised = 0;
        s.Changed += () => raised++;

        s.AddOrUpdate(@"C:\a.fits");
        s.Remove(@"C:\a.fits");
        s.Clear();

        Assert.Equal(3, raised);
    }

    // ── The three viewers ────────────────────────────────────────────────────

    /// <summary>
    /// Each viewer is the same store with its own folder and cap — and each keeps its own file, so one
    /// viewer's history never appears in another's empty state.
    /// </summary>
    [Fact]
    public void EachViewerKeepsItsOwnList()
    {
        var cubes = new RecentCubesService(Store("cubes.json"));
        var notebooks = new RecentNotebooksService(Store("notebooks.json"));
        var images = new RecentFitsService(Store("fits.json"));

        cubes.AddOrUpdate(@"C:\a.fits");
        notebooks.AddOrUpdate(@"C:\b.ipynb");
        images.AddOrUpdate(@"C:\c.fits");

        Assert.Equal([@"C:\a.fits"], cubes.Entries.Select(e => e.Path).ToArray());
        Assert.Equal([@"C:\b.ipynb"], notebooks.Entries.Select(e => e.Path).ToArray());
        Assert.Equal([@"C:\c.fits"], images.Entries.Select(e => e.Path).ToArray());
    }

    /// <summary>The caps the two originals had are the caps they still have.</summary>
    [Fact]
    public void TheViewersKeepTheirOwnCaps()
    {
        var cubes = new RecentCubesService(Store("cubes2.json"));
        var notebooks = new RecentNotebooksService(Store("notebooks2.json"));

        for (var i = 0; i < 20; i++)
        {
            cubes.AddOrUpdate($@"C:\c{i}.fits");
            notebooks.AddOrUpdate($@"C:\n{i}.ipynb");
        }

        Assert.Equal(8, cubes.Entries.Count);
        Assert.Equal(15, notebooks.Entries.Count);
    }

    /// <summary>The FITS viewer's list is the cube's shape, because they are the same kind of list.</summary>
    [Fact]
    public void TheFitsListHasTheSameCapAsTheCubes()
    {
        var images = new RecentFitsService(Store("fits2.json"));
        for (var i = 0; i < 20; i++) images.AddOrUpdate($@"C:\f{i}.fits");

        Assert.Equal(8, images.Entries.Count);
    }
}
