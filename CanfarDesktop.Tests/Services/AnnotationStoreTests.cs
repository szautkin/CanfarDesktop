using Xunit;
using CanfarDesktop.Models;
using CanfarDesktop.Services.Fits;

namespace CanfarDesktop.Tests.Services;

/// <summary>
/// Marks on disk, keyed by the file they were drawn on. The store is constructed with an explicit path
/// here; in the app it is the local folder.
/// </summary>
public class AnnotationStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "verbinal-annotations-" + Guid.NewGuid().ToString("N"));

    private AnnotationStore Store() => new(Path.Combine(_dir, "annotations.json"));

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private static Annotation Mark(string id, double x = 10, double y = 20) => new()
    {
        Id = id,
        Kind = AnnotationKind.Circle,
        Anchor = AnnotationAnchor.ImagePixel(x, y),
        Extent = Extent.Square(5),
        CreatedAt = "2026-09-05T12:00:00Z",
    };

    [Fact]
    public void MarksComeBackOnTheFileTheyWereDrawnOn()
    {
        var store = Store();
        store.Add("C:/data/m31.fits", Mark("a"));

        var back = Store().LoadFor("C:/data/m31.fits");

        Assert.Single(back);
        Assert.Equal("a", back[0].Id);
        Assert.Equal(AnnotationKind.Circle, back[0].Kind);
        Assert.Equal(5, back[0].Extent!.HalfWidth);
    }

    /// <summary>Two FITS files never show each other's marks.</summary>
    [Fact]
    public void TargetsAreKeptApart()
    {
        var store = Store();
        store.Add("a.fits", Mark("one"));
        store.Add("b.fits", Mark("two"));

        Assert.Equal("one", Assert.Single(store.LoadFor("a.fits")).Id);
        Assert.Equal("two", Assert.Single(store.LoadFor("b.fits")).Id);
        Assert.Empty(store.LoadFor("c.fits"));
    }

    /// <summary>
    /// Saving one target rewrites the whole file, so the test that matters is that it did not take
    /// another target's work with it.
    /// </summary>
    [Fact]
    public void SavingOneTargetLeavesTheOthersAlone()
    {
        var store = Store();
        store.Add("a.fits", Mark("keep"));
        store.SaveFor("b.fits", [Mark("new")]);

        Assert.Equal("keep", Assert.Single(store.LoadFor("a.fits")).Id);
    }

    [Fact]
    public void AnEmptySaveForgetsTheTargetRatherThanStoringNothing()
    {
        var store = Store();
        store.Add("a.fits", Mark("one"));
        store.SaveFor("a.fits", []);

        Assert.Empty(store.LoadFor("a.fits"));
        Assert.DoesNotContain("a.fits", store.Targets());
    }

    [Fact]
    public void AMarkThatCannotBeDrawnIsRefusedWithTheReason()
    {
        var store = Store();
        var sizeless = Mark("bad") with { Extent = null };

        var ex = Assert.Throws<ArgumentException>(() => store.Add("a.fits", sizeless));
        Assert.Contains("needs a size", ex.Message);
        Assert.Empty(store.LoadFor("a.fits"));
    }

    [Fact]
    public void UpdatingReplacesInPlaceAndKeepsTheOrder()
    {
        var store = Store();
        store.Add("a.fits", Mark("one"));
        store.Add("a.fits", Mark("two"));

        var after = store.Update("a.fits", Mark("one", x: 99));

        Assert.NotNull(after);
        Assert.Equal(["one", "two"], after!.Select(a => a.Id));
        Assert.Equal(99, after[0].Anchor.X);
    }

    [Fact]
    public void UpdatingSomethingThatIsNotThereSaysSoRatherThanAddingIt()
    {
        var store = Store();
        store.Add("a.fits", Mark("one"));

        Assert.Null(store.Update("a.fits", Mark("missing")));
        Assert.Single(store.LoadFor("a.fits"));
    }

    [Fact]
    public void RemovingReportsWhetherThereWasAnythingToRemove()
    {
        var store = Store();
        store.Add("a.fits", Mark("one"));

        Assert.True(store.Remove("a.fits", "one"));
        Assert.False(store.Remove("a.fits", "one"));
        Assert.Empty(store.LoadFor("a.fits"));
    }

    /// <summary>
    /// A viewer must open whether or not this file is readable. Losing annotations is a disappointment;
    /// refusing to show an image because of them would be a bug.
    /// </summary>
    [Fact]
    public void ACorruptFileIsAnEmptySetNotAFailure()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "annotations.json"), "{ not json at all");

        Assert.Empty(Store().LoadFor("a.fits"));
    }

    [Fact]
    public void AStoreWithNothingWrittenYetReadsAsEmpty()
    {
        var store = new AnnotationStore(Path.Combine(_dir, "never-written", "annotations.json"));
        Assert.Empty(store.LoadFor("a.fits"));
        Assert.Empty(store.Targets());
    }

    /// <summary>
    /// Reading must never fail — a viewer has to open whether or not this file is readable. Saving is
    /// the other way round: a save that quietly did nothing loses a drawing somebody made, and they
    /// find out the next time they open the file.
    /// </summary>
    [Fact]
    public void ASaveThatCannotBeWrittenIsRaisedRatherThanSwallowed()
    {
        var unwritable = new AnnotationStore(Path.Combine(_dir, "wall\0bad", "annotations.json"));

        Assert.Empty(unwritable.LoadFor("a.fits"));
        Assert.ThrowsAny<Exception>(() => unwritable.Add("a.fits", Mark("one")));
    }

    /// <summary>
    /// A style is optional on disk, and a mark stored without one must load without one — defaulting it
    /// would restyle every agent mark to the user colour, silently, with nothing to notice.
    /// </summary>
    [Fact]
    public void AMarkStoredWithoutAStyleLoadsWithoutOne()
    {
        var store = Store();
        store.Add("a.fits", Mark("plain") with { Author = MarkAuthor.Agent, Style = null });

        var back = Assert.Single(Store().LoadFor("a.fits"));

        Assert.Null(back.Style);
        Assert.Equal(MarkAuthor.Agent, back.Author);
        Assert.Equal(MarkStyle.AgentDefault.ColourHex(), back.EffectiveStyle.ColourHex());
    }

    [Fact]
    public void AStyleThatWasSaidExplicitlySurvivesTheRoundTrip()
    {
        var store = Store();
        var styled = Mark("styled") with { Style = MarkStyle.UserDefault.WithColourHex("#ff8800") with { Bold = true, Stroke = 3 } };
        store.Add("a.fits", styled);

        var back = Assert.Single(Store().LoadFor("a.fits"));

        Assert.Equal("#ff8800", back.Style!.ColourHex());
        Assert.True(back.Style.Bold);
        Assert.Equal(3, back.Style.Stroke);
    }

    /// <summary>Sky anchors are how a mark points at the same place in a different image of the field.</summary>
    [Fact]
    public void AnAnchorKeepsItsSpaceAcrossTheRoundTrip()
    {
        var store = Store();
        store.Add("a.fits", Mark("sky") with { Anchor = AnnotationAnchor.Sky(10.6847, 41.2687) });

        var back = Assert.Single(Store().LoadFor("a.fits"));

        Assert.Equal(AnchorSpace.Sky, back.Anchor.Space);
        Assert.Equal(10.6847, back.Anchor.X, 6);
        Assert.Equal(41.2687, back.Anchor.Y, 6);
    }
}
