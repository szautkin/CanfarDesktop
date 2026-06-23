using Xunit;
using CanfarDesktop.Models;
using CanfarDesktop.Services.Database;

namespace CanfarDesktop.Tests.Services;

public class ObservationNoteStoreTests : IDisposable
{
    private readonly AppDatabase _db = new(filePath: null); // private in-memory database
    private readonly ObservationNoteStore _store;

    public ObservationNoteStoreTests() => _store = new ObservationNoteStore(_db);

    public void Dispose() => _db.Dispose();

    private static ObservationNote Note(string id, string note = "", int rating = 0, params string[] tags)
        => new() { PublisherID = id, Note = note, Rating = rating, Tags = tags };

    [Fact]
    public void Upsert_ThenGet_RoundTrips()
    {
        _store.Upsert(Note("caom:CFHT/1", "a spiral galaxy", 4, "galaxy", "spiral"));
        var got = _store.Get("caom:CFHT/1");

        Assert.NotNull(got);
        Assert.Equal("a spiral galaxy", got!.Note);
        Assert.Equal(4, got.Rating);
        Assert.Equal(new[] { "galaxy", "spiral" }, got.Tags);
    }

    [Fact]
    public void Upsert_UpdatesExistingRow()
    {
        _store.Upsert(Note("caom:CFHT/2", "first", 1));
        _store.Upsert(Note("caom:CFHT/2", "second", 5, "x"));

        var got = _store.Get("caom:CFHT/2");
        Assert.Equal("second", got!.Note);
        Assert.Equal(5, got.Rating);
        Assert.Equal(new[] { "x" }, got.Tags);
        Assert.Single(_store.All());
    }

    [Fact]
    public void EmptyNote_RemovesRow()
    {
        _store.Upsert(Note("caom:CFHT/3", "temp", 2));
        Assert.NotNull(_store.Get("caom:CFHT/3"));

        _store.Upsert(Note("caom:CFHT/3")); // blank, unrated, no tags
        Assert.Null(_store.Get("caom:CFHT/3"));
        Assert.Empty(_store.All());
    }

    [Fact]
    public void Fts_FindsByNoteWord_AndTagWord_WithPrefix()
    {
        _store.Upsert(Note("caom:A/1", "deep imaging of Andromeda", 0, "galaxy"));
        _store.Upsert(Note("caom:B/2", "calibration dark frame", 0, "calib"));

        Assert.Equal(new[] { "caom:A/1" }, _store.SearchPublisherIds("andromeda"));
        Assert.Equal(new[] { "caom:A/1" }, _store.SearchPublisherIds("gal"));     // prefix on tag
        Assert.Equal(new[] { "caom:B/2" }, _store.SearchPublisherIds("dark"));
        Assert.Empty(_store.SearchPublisherIds("supernova"));
    }

    [Fact]
    public void SoftDelete_HidesFromGetSearchAndAll()
    {
        _store.Upsert(Note("caom:A/9", "transient candidate", 3, "transient"));
        _store.Delete("caom:A/9");

        Assert.Null(_store.Get("caom:A/9"));
        Assert.Empty(_store.SearchPublisherIds("transient"));
        Assert.Empty(_store.All());
    }

    [Fact]
    public void Search_WithOperatorChars_DoesNotThrow()
    {
        _store.Upsert(Note("caom:A/1", "ngc 1234 observation", 0));
        // FTS operators / quotes in user input must be neutralized, not injected.
        Assert.Empty(_store.SearchPublisherIds("\" OR 1=1 --"));
        Assert.Equal(new[] { "caom:A/1" }, _store.SearchPublisherIds("ngc\""));
    }
}
