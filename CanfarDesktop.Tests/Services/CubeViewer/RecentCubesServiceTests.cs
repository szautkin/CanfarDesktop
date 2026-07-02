using Xunit;
using CanfarDesktop.Services.CubeViewer;

namespace CanfarDesktop.Tests.Services.CubeViewer;

/// <summary>
/// The recently-opened-cubes store: dedupe/ordering, the 8-entry cap, persistence, and the
/// skip-missing-files-on-load rule. Each test gets its own temp store file so tests can't
/// pollute each other (or the developer's real recents).
/// </summary>
public class RecentCubesServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly string _storePath;

    public RecentCubesServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "canfar-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _storePath = Path.Combine(_dir, "recent-cubes.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private RecentCubesService NewService() => new(_storePath);

    /// <summary>Create a real (empty) file so the load-time existence filter keeps its entry.</summary>
    private string TouchFile(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "");
        return path;
    }

    [Fact]
    public void AddOrUpdate_AddsEntry_DefaultNameIsFileName()
    {
        var s = NewService();

        s.AddOrUpdate(@"C:\data\ngc1300.fits");

        Assert.Single(s.Entries);
        Assert.Equal("ngc1300.fits", s.Entries[0].Name);
    }

    [Fact]
    public void AddOrUpdate_UsesDisplayName_WhenProvided()
    {
        var s = NewService();

        s.AddOrUpdate(@"C:\data\cube.fits", "NGC 1300");

        Assert.Equal("NGC 1300", s.Entries[0].Name);
    }

    [Fact]
    public void AddOrUpdate_DuplicatePath_MovesToTop_CaseInsensitive()
    {
        var s = NewService();
        s.AddOrUpdate(@"C:\data\a.fits");
        s.AddOrUpdate(@"C:\data\b.fits");

        s.AddOrUpdate(@"c:\DATA\A.fits");

        Assert.Equal(2, s.Entries.Count);
        Assert.Equal(@"c:\DATA\A.fits", s.Entries[0].Path);
    }

    [Fact]
    public void AddOrUpdate_CapsAtEight_EvictingOldest()
    {
        var s = NewService();

        for (int i = 0; i < 12; i++)
            s.AddOrUpdate($@"C:\data\cube{i}.fits");

        Assert.Equal(8, s.Entries.Count);
        Assert.Equal("cube11.fits", s.Entries[0].Name);  // most recent first
        Assert.Equal("cube4.fits", s.Entries[^1].Name);  // 0..3 evicted
    }

    [Fact]
    public void Load_SkipsEntriesWhoseFileIsGone()
    {
        var real1 = TouchFile("a.fits");
        var real2 = TouchFile("b.fits");
        var s = NewService();
        s.AddOrUpdate(real1);
        s.AddOrUpdate(@"C:\data\deleted-since.fits"); // never existed on disk
        s.AddOrUpdate(real2);

        var reloaded = NewService();

        Assert.Equal(2, reloaded.Entries.Count);
        Assert.Equal(real2, reloaded.Entries[0].Path);
        Assert.Equal(real1, reloaded.Entries[1].Path);
    }

    [Fact]
    public void Load_RoundTripsNameAndPath()
    {
        var real = TouchFile("m51.fits");
        var s = NewService();
        s.AddOrUpdate(real, "M 51");

        var reloaded = NewService();

        Assert.Single(reloaded.Entries);
        Assert.Equal("M 51", reloaded.Entries[0].Name);
        Assert.Equal(real, reloaded.Entries[0].Path);
    }

    [Fact]
    public void Remove_DeletesEntry()
    {
        var s = NewService();
        s.AddOrUpdate(@"C:\data\a.fits");
        s.AddOrUpdate(@"C:\data\b.fits");

        s.Remove(@"C:\data\a.fits");

        Assert.Single(s.Entries);
        Assert.Equal("b.fits", s.Entries[0].Name);
    }

    [Fact]
    public void Clear_RemovesAll()
    {
        var s = NewService();
        s.AddOrUpdate(@"C:\data\a.fits");

        s.Clear();

        Assert.Empty(s.Entries);
    }

    [Fact]
    public void Changed_FiresOnMutation()
    {
        var s = NewService();
        int fired = 0;
        s.Changed += () => fired++;

        s.AddOrUpdate(@"C:\data\a.fits");
        s.Remove(@"C:\data\a.fits");
        s.Clear();

        Assert.Equal(3, fired);
    }

    [Fact]
    public void Load_CorruptStore_YieldsEmptyList()
    {
        File.WriteAllText(_storePath, "{ not json ]");

        Assert.Empty(NewService().Entries);
    }
}
