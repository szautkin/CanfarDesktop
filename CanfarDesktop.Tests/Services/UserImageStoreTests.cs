using Xunit;
using CanfarDesktop.Models;
using CanfarDesktop.Services.ImageDiscovery;

namespace CanfarDesktop.Tests.Services;

/// <summary>The images someone added by hand: a list, a decision, and one place that holds it.</summary>
public class UserImageStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "verbinal-userimages-" + Guid.NewGuid().ToString("N"));

    private UserImageStore Store() => new(Path.Combine(_dir, "user_images.json"));

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private static RegistryImage Image(string id, params string[] types) => new(id, types);

    [Fact]
    public void AnAddedImageComesBackWithItsTypes()
    {
        var store = Store();
        store.Add(Image("host/p/n:1", "notebook", "carta"));

        var back = Assert.Single(Store().All());

        Assert.Equal("host/p/n:1", back.Id);
        Assert.Equal(["notebook", "carta"], back.Types);
        Assert.NotNull(back.AddedAt);   // stamped on the way in: it is what makes it a list entry
    }

    /// <summary>Newest first: the last thing added is the thing being looked for.</summary>
    [Fact]
    public void TheListIsNewestFirst()
    {
        var store = Store();
        store.Add(Image("host/p/first:1"));
        store.Add(Image("host/p/second:1"));

        Assert.Equal(["host/p/second:1", "host/p/first:1"], store.All().Select(i => i.Id));
    }

    /// <summary>Adding something already there is an answer, not a failure — and not a duplicate.</summary>
    [Fact]
    public void AddingTheSameImageTwiceChangesNothing()
    {
        var store = Store();

        Assert.True(store.Add(Image("host/p/n:1")));
        Assert.False(store.Add(Image("host/p/n:1")));
        Assert.Single(store.All());
    }

    [Fact]
    public void AReferenceIsMatchedWhateverItsCase()
    {
        var store = Store();
        store.Add(Image("Host/P/N:1"));

        Assert.False(store.Add(Image("host/p/n:1")));
        Assert.True(store.Remove("HOST/P/N:1"));
    }

    [Fact]
    public void RemovingReportsWhetherThereWasAnythingToRemove()
    {
        var store = Store();
        store.Add(Image("host/p/n:1"));

        Assert.True(store.Remove("host/p/n:1"));
        Assert.False(store.Remove("host/p/n:1"));
        Assert.Empty(store.All());
    }

    [Fact]
    public void AnImageWithNoReferenceIsNotAnImage()
    {
        var store = Store();

        Assert.False(store.Add(Image("   ")));
        Assert.False(store.Remove(""));
        Assert.Empty(store.All());
    }

    /// <summary>
    /// Three surfaces read this list — the images card, the package search and the launch form — so a
    /// change has to reach them. Without the event they each keep the list they last read.
    /// </summary>
    [Fact]
    public void EveryChangeIsAnnounced()
    {
        var store = Store();
        var changes = 0;
        store.Changed += () => changes++;

        store.Add(Image("host/p/n:1"));
        store.Add(Image("host/p/n:1"));   // already there: nothing changed, nothing announced
        store.Remove("host/p/n:1");
        store.Remove("host/p/n:1");       // gone already

        Assert.Equal(2, changes);
    }

    [Fact]
    public void AnUnreadableFileIsAnEmptyListRatherThanAFailure()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "user_images.json"), "{ not json");

        Assert.Empty(Store().All());
    }
}
