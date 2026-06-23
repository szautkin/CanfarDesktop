using Xunit;
using CanfarDesktop.Models.ImageDiscovery;
using CanfarDesktop.Services.ImageDiscovery;

namespace CanfarDesktop.Tests.Services;

public class JsonManifestStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "verbinal_md_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private static ImageManifest M(string id, string osFamily, params string[] python)
        => new()
        {
            ImageID = id,
            OsFamily = osFamily,
            OsVersion = "22.04",
            CapturedAt = DateTimeOffset.UtcNow,
            PythonPackages = python.Select(p => new PythonPackage(p, "1", "pip", "base")).ToArray(),
        };

    [Fact]
    public void SetManifest_ThenOutcome_Success()
    {
        var store = new JsonManifestStore(_dir);
        store.SetManifest(M("img:1", "ubuntu", "astropy"));

        var o = store.Outcome("img:1");
        Assert.NotNull(o);
        Assert.True(o!.IsSuccess);
        Assert.Equal("img:1", o.Manifest!.ImageID);
        Assert.Equal(1, store.Count());
    }

    [Fact]
    public void SetFailure_ThenOutcome_Failure()
    {
        var store = new JsonManifestStore(_dir);
        store.SetFailure("img:2", FailureCategory.JobTimedOut, "timed out", DateTimeOffset.UtcNow, "job-9");

        var o = store.Outcome("img:2");
        Assert.NotNull(o);
        Assert.False(o!.IsSuccess);
        Assert.Equal(FailureCategory.JobTimedOut, o.Category);
        Assert.Equal("job-9", o.JobID);
    }

    [Fact]
    public void Invalidate_And_Clear()
    {
        var store = new JsonManifestStore(_dir);
        store.SetManifest(M("a:1", "ubuntu"));
        store.SetManifest(M("b:1", "ubuntu"));

        store.Invalidate("a:1");
        Assert.Null(store.Outcome("a:1"));
        Assert.Equal(1, store.Count());

        store.Clear();
        Assert.Equal(0, store.Count());
    }

    [Fact]
    public void Persists_AcrossReload()
    {
        new JsonManifestStore(_dir).SetManifest(M("keep:1", "ubuntu", "numpy"));

        var reopened = new JsonManifestStore(_dir);
        var o = reopened.Outcome("keep:1");
        Assert.NotNull(o);
        Assert.True(o!.IsSuccess);
        Assert.Contains(o.Manifest!.PythonPackages, p => p.Name == "numpy");
    }

    [Fact]
    public void Search_And_SearchPartial()
    {
        var store = new JsonManifestStore(_dir);
        store.SetManifest(M("a:1", "ubuntu", "astropy", "numpy"));
        store.SetManifest(M("b:1", "ubuntu", "astropy"));
        store.SetFailure("c:1", FailureCategory.Unknown, "x", DateTimeOffset.UtcNow, null);

        Assert.Equal(new[] { "a:1" }, store.Search(new PackageQuery { Python = { "astropy", "numpy" } }));

        var partial = store.SearchPartial(new PackageQuery { Python = { "astropy", "numpy" } }, 0.0, 10);
        Assert.Equal(2, partial.Count);
        Assert.Equal("a:1", partial[0].ImageID);            // best score first
        Assert.True(partial[0].Score > partial[1].Score);

        Assert.Equal(new[] { "a:1", "b:1", "c:1" }, store.KnownImages()); // failures included
        Assert.Equal(3, store.Count());
    }

    [Fact]
    public void AllPackages_AggregatesAcrossManifests()
    {
        var store = new JsonManifestStore(_dir);
        store.SetManifest(M("a:1", "ubuntu", "astropy"));
        store.SetManifest(M("b:1", "almalinux", "scipy"));

        var all = store.AllPackages();
        Assert.Contains("ubuntu", all.OsFamilies);
        Assert.Contains("almalinux", all.OsFamilies);
        Assert.Contains("astropy", all.Python);
        Assert.Contains("scipy", all.Python);
    }
}
