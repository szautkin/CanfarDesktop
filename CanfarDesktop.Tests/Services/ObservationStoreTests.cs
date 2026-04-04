using Xunit;
using CanfarDesktop.Models;
using CanfarDesktop.Services;

namespace CanfarDesktop.Tests.Services;

public class ObservationStoreTests
{
    private static ObservationStore CreateStore() => new();

    private static DownloadedObservation MakeObs(string pubId = "ivo://test/1", string collection = "TEST",
        string target = "M31") => new()
    {
        PublisherID = pubId,
        Collection = collection,
        TargetName = target,
        ObservationID = "obs-1",
        Instrument = "Camera",
        LocalPath = "" // no real file
    };

    [Fact]
    public void Save_AddsObservation()
    {
        var store = CreateStore();
        var obs = MakeObs();
        store.Save(obs);
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void Save_DeduplicatesByPublisherID()
    {
        var store = CreateStore();
        store.Save(MakeObs("pub-1", target: "M31"));
        store.Save(MakeObs("pub-1", target: "M33"));

        Assert.Equal(1, store.Count);
        Assert.Equal("M33", store.Observations[0].TargetName); // Updated
    }

    [Fact]
    public void Save_InsertsAtFront()
    {
        var store = CreateStore();
        store.Save(MakeObs("pub-1", target: "first"));
        store.Save(MakeObs("pub-2", target: "second"));

        Assert.Equal("second", store.Observations[0].TargetName);
    }

    [Fact]
    public void Remove_ByIdNotPublisherID()
    {
        var store = CreateStore();
        var obs = MakeObs();
        store.Save(obs);
        Assert.Equal(1, store.Count);

        store.Remove(obs);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void Remove_WrongId_DoesNothing()
    {
        var store = CreateStore();
        store.Save(MakeObs("pub-1"));

        var fake = new DownloadedObservation { Id = "nonexistent" };
        store.Remove(fake);

        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void Clear_RemovesAll()
    {
        var store = CreateStore();
        store.Save(MakeObs("pub-1"));
        store.Save(MakeObs("pub-2"));
        store.Clear();
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void Contains_TrueWhenPresent()
    {
        var store = CreateStore();
        store.Save(MakeObs("pub-1"));
        Assert.True(store.Contains("pub-1"));
        Assert.False(store.Contains("pub-2"));
    }

    [Fact]
    public void Filter_ByTargetName()
    {
        var store = CreateStore();
        store.Save(MakeObs("pub-1", target: "M31"));
        store.Save(MakeObs("pub-2", target: "NGC 1234"));

        var results = store.Filter("M31");
        Assert.Single(results);
        Assert.Equal("M31", results[0].TargetName);
    }

    [Fact]
    public void Filter_CaseInsensitive()
    {
        var store = CreateStore();
        store.Save(MakeObs("pub-1", target: "Andromeda"));

        Assert.Single(store.Filter("andromeda"));
        Assert.Single(store.Filter("ANDROMEDA"));
    }

    [Fact]
    public void Filter_EmptyText_ReturnsAll()
    {
        var store = CreateStore();
        store.Save(MakeObs("pub-1"));
        store.Save(MakeObs("pub-2"));

        Assert.Equal(2, store.Filter("").Count);
        Assert.Equal(2, store.Filter("  ").Count);
    }

    [Fact]
    public void Filter_ByCollection()
    {
        var store = CreateStore();
        store.Save(MakeObs("pub-1", "CFHT", "M31"));
        store.Save(MakeObs("pub-2", "JWST", "M33"));

        Assert.Single(store.Filter("JWST"));
    }

    [Fact]
    public void GroupByCollection_GroupsCorrectly()
    {
        var store = CreateStore();
        store.Save(MakeObs("pub-1", "CFHT"));
        store.Save(MakeObs("pub-2", "CFHT"));
        store.Save(MakeObs("pub-3", "JWST"));

        var groups = store.GroupByCollection();
        Assert.Equal(2, groups.Count);
        Assert.Equal(2, groups["CFHT"].Count);
        Assert.Single(groups["JWST"]);
    }

    [Fact]
    public void Observations_ReturnsDefensiveCopy()
    {
        var store = CreateStore();
        store.Save(MakeObs("pub-1"));

        var snapshot = store.Observations;
        Assert.Single(snapshot);

        // Adding to store should not affect the snapshot
        store.Save(MakeObs("pub-2"));
        Assert.Single(snapshot); // snapshot unchanged
        Assert.Equal(2, store.Count); // store updated
    }

    [Fact]
    public void Filter_ByInstrument()
    {
        var store = CreateStore();
        store.Save(new DownloadedObservation { PublisherID = "p1", Instrument = "MegaCam", TargetName = "X" });
        store.Save(new DownloadedObservation { PublisherID = "p2", Instrument = "WFC3", TargetName = "Y" });

        Assert.Single(store.Filter("MegaCam"));
        Assert.Single(store.Filter("WFC3"));
    }

    [Fact]
    public void Filter_ByObservationID()
    {
        var store = CreateStore();
        store.Save(new DownloadedObservation { PublisherID = "p1", ObservationID = "jw01837001", TargetName = "X" });

        Assert.Single(store.Filter("jw01837"));
        Assert.Empty(store.Filter("nonexistent"));
    }

    [Fact]
    public void Contains_EmptyPublisherID_ReturnsFalse()
    {
        var store = CreateStore();
        Assert.False(store.Contains(""));
        Assert.False(store.Contains("anything"));
    }

    [Fact]
    public void GroupByCollection_EmptyStore_ReturnsEmptyDict()
    {
        var store = CreateStore();
        Assert.Empty(store.GroupByCollection());
    }

    [Fact]
    public void Save_EmptyPublisherID_StillSaves()
    {
        var store = CreateStore();
        store.Save(new DownloadedObservation { PublisherID = "", TargetName = "test" });
        Assert.Equal(1, store.Count);
    }
}
