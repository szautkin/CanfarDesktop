using System.Text;
using Xunit;
using CanfarDesktop.Models;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Read;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

public class ReadToolTests
{
    private static readonly McpToolContext Ctx = McpToolContext.ForExternal("Claude/1.0", Guid.Empty);

    private static JsonObject Data(ToolResult r) => (JsonObject)JsonValue.Parse(Encoding.UTF8.GetString(Assert.IsType<DataResult>(r).Json));
    private static long Count(JsonObject o) => ((JsonInt)o["count"]!).Value;

    // ── Research read tools ───────────────────────────────────────────────────

    [Fact]
    public async Task ListDownloaded_ReturnsSummaries()
    {
        var tool = new ListDownloadedObservationsTool(() => new List<DownloadedObservation>
        {
            new() { Id = "1", TargetName = "M31", Collection = "CFHT" },
            new() { Id = "2" },
        });
        var data = Data(await tool.InvokeAsync(JsonValue.Null, Ctx, default));
        Assert.Equal(2, Count(data));
        Assert.Equal(2, ((JsonArray)data["observations"]!).Items.Count);
    }

    [Fact]
    public async Task GetDownloaded_Found_ReturnsObservation()
    {
        var tool = new GetDownloadedObservationTool(() => new List<DownloadedObservation> { new() { Id = "abc", TargetName = "M31" } });
        var data = Data(await tool.InvokeAsync(JsonValue.Parse("""{"id":"abc"}"""), Ctx, default));
        Assert.Equal("M31", ((JsonString)data["targetName"]!).Value);
    }

    [Fact]
    public async Task GetDownloaded_NotFound_UnknownTarget()
    {
        var tool = new GetDownloadedObservationTool(() => new List<DownloadedObservation> { new() { Id = "abc" } });
        var result = await tool.InvokeAsync(JsonValue.Parse("""{"id":"nope"}"""), Ctx, default);
        Assert.IsType<UnknownTarget>(Assert.IsType<FailedResult>(result).Reason);
    }

    [Fact]
    public async Task GetDownloaded_MissingId_InvalidArgument()
    {
        var tool = new GetDownloadedObservationTool(() => Array.Empty<DownloadedObservation>());
        var result = await tool.InvokeAsync(JsonValue.Parse("""{"id":""}"""), Ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
    }

    [Fact]
    public async Task GetNotes_AllAndFiltered()
    {
        var tool = new GetObservationNotesTool(() => new List<ObservationNote>
        {
            new() { PublisherID = "a", Note = "x", Rating = 5, Tags = new[] { "deep" } },
            new() { PublisherID = "b", Note = "y" },
        });
        Assert.Equal(2, Count(Data(await tool.InvokeAsync(JsonValue.Null, Ctx, default))));
        Assert.Equal(1, Count(Data(await tool.InvokeAsync(JsonValue.Parse("""{"publisherId":"a"}"""), Ctx, default))));
    }

    // ── Search read tools ─────────────────────────────────────────────────────

    [Fact]
    public async Task ListSavedQueries_ReturnsNameAndAdql()
    {
        var tool = new ListSavedQueriesTool(() => new List<SavedQuery> { new() { Name = "Q", Adql = "SELECT 1" } });
        var data = Data(await tool.InvokeAsync(JsonValue.Null, Ctx, default));
        Assert.Equal(1, Count(data));
        var q = (JsonObject)((JsonArray)data["queries"]!).Items[0];
        Assert.Equal("Q", ((JsonString)q["name"]!).Value);
        Assert.Equal("SELECT 1", ((JsonString)q["adql"]!).Value);
    }

    [Fact]
    public async Task ListRecentSearches_HonorsLimit()
    {
        var tool = new ListRecentSearchesTool(() => new List<RecentSearch>
        {
            new() { Summary = "a", ResultCount = 1 },
            new() { Summary = "b", ResultCount = 2 },
            new() { Summary = "c", ResultCount = 3 },
        });
        Assert.Equal(3, Count(Data(await tool.InvokeAsync(JsonValue.Null, Ctx, default))));
        Assert.Equal(2, Count(Data(await tool.InvokeAsync(JsonValue.Parse("""{"limit":2}"""), Ctx, default))));
    }
}
