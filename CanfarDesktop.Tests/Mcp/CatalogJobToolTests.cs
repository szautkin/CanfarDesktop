using System.Text;
using Xunit;
using CanfarDesktop.Models;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Read;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

public class CatalogJobToolTests
{
    private static readonly McpToolContext Ctx = McpToolContext.ForExternal("Claude/1.0", Guid.Empty);

    private static JsonObject Data(ToolResult r) => (JsonObject)JsonValue.Parse(Encoding.UTF8.GetString(Assert.IsType<DataResult>(r).Json));

    private static List<RawImage> SampleImages() => new()
    {
        new RawImage { Id = "images.canfar.net/skaha/astroml:24.07", Types = new[] { "notebook" } },
        new RawImage { Id = "images.canfar.net/skaha/desktop:1.0", Types = new[] { "desktop" } },
        new RawImage { Id = "images.canfar.net/skaha/batch:2.0", Types = new[] { "headless" } },
    };

    [Fact]
    public async Task ListSessionImages_ReturnsIdsAndTypes()
    {
        var tool = new ListSessionImagesTool(_ => Task.FromResult(SampleImages()));

        var data = Data(await tool.InvokeAsync(JsonValue.Null, Ctx, default));

        Assert.Equal(3, ((JsonInt)data["count"]!).Value);
        var first = (JsonObject)((JsonArray)data["images"]!).Items[0];
        Assert.Equal("images.canfar.net/skaha/astroml:24.07", ((JsonString)first["id"]!).Value);
        var types = ((JsonArray)first["types"]!).Items.Select(t => ((JsonString)t).Value).ToList();
        Assert.Contains("notebook", types);
    }

    [Fact]
    public async Task ListSessionImages_FiltersByType_CaseInsensitive()
    {
        var tool = new ListSessionImagesTool(_ => Task.FromResult(SampleImages()));

        var data = Data(await tool.InvokeAsync(JsonValue.Parse("""{"type":"HEADLESS"}"""), Ctx, default));

        Assert.Equal(1, ((JsonInt)data["count"]!).Value);
        var only = (JsonObject)((JsonArray)data["images"]!).Items[0];
        Assert.Equal("images.canfar.net/skaha/batch:2.0", ((JsonString)only["id"]!).Value);
    }

    [Fact]
    public async Task ListRecentLaunches_ReturnsFieldsMostRecentFirst()
    {
        var launches = new List<RecentLaunch>
        {
            new() { Name = "old", Type = "notebook", Image = "img:1", ImageLabel = "img 1", Project = "skaha",
                    ResourceType = "flexible", Cores = 2, Ram = 8, Gpus = 0, LaunchedAt = new DateTime(2026, 1, 1) },
            new() { Name = "new", Type = "desktop", Image = "img:2", ImageLabel = "img 2", Project = "skaha",
                    ResourceType = "fixed", Cores = 4, Ram = 16, Gpus = 1, LaunchedAt = new DateTime(2026, 6, 1) },
        };
        var tool = new ListRecentLaunchesTool(() => launches);

        var data = Data(await tool.InvokeAsync(JsonValue.Null, Ctx, default));

        Assert.Equal(2, ((JsonInt)data["count"]!).Value);
        var first = (JsonObject)((JsonArray)data["launches"]!).Items[0];
        Assert.Equal("new", ((JsonString)first["name"]!).Value);
        Assert.Equal("desktop", ((JsonString)first["type"]!).Value);
        Assert.Equal(4, ((JsonInt)first["cores"]!).Value);
        Assert.Equal(16, ((JsonInt)first["ram"]!).Value);
        Assert.Equal(1, ((JsonInt)first["gpus"]!).Value);
    }

    [Fact]
    public async Task ListRecentLaunches_AppliesLimit()
    {
        var launches = new List<RecentLaunch>
        {
            new() { Name = "a", LaunchedAt = new DateTime(2026, 1, 1) },
            new() { Name = "b", LaunchedAt = new DateTime(2026, 2, 1) },
            new() { Name = "c", LaunchedAt = new DateTime(2026, 3, 1) },
        };
        var tool = new ListRecentLaunchesTool(() => launches);

        var data = Data(await tool.InvokeAsync(JsonValue.Parse("""{"limit":1}"""), Ctx, default));

        Assert.Equal(1, ((JsonInt)data["count"]!).Value);
        var first = (JsonObject)((JsonArray)data["launches"]!).Items[0];
        Assert.Equal("c", ((JsonString)first["name"]!).Value);
    }

    [Fact]
    public async Task ListRecentLaunches_RejectsNonPositiveLimit()
    {
        var tool = new ListRecentLaunchesTool(() => new List<RecentLaunch>());
        var result = await tool.InvokeAsync(JsonValue.Parse("""{"limit":0}"""), Ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
    }

    [Fact]
    public async Task ListHeadlessJobs_FiltersToHeadless()
    {
        var sessions = new List<Session>
        {
            new() { Id = "s1", SessionName = "nb", SessionType = "notebook", Status = "Running" },
            new() { Id = "s2", SessionName = "job", SessionType = "Headless", Status = "Succeeded",
                    ContainerImage = "img:1", StartedTime = "t0", ExpiresTime = "t1" },
        };
        var tool = new ListHeadlessJobsTool(_ => Task.FromResult<IReadOnlyList<Session>>(sessions));

        var data = Data(await tool.InvokeAsync(JsonValue.Null, Ctx, default));

        Assert.Equal(1, ((JsonInt)data["count"]!).Value);
        var job = (JsonObject)((JsonArray)data["jobs"]!).Items[0];
        Assert.Equal("s2", ((JsonString)job["id"]!).Value);
        Assert.Equal("job", ((JsonString)job["name"]!).Value);
        Assert.Equal("Succeeded", ((JsonString)job["status"]!).Value);
    }

    [Fact]
    public async Task GetHeadlessJobLogs_FoundAndMissing()
    {
        var tool = new GetHeadlessJobLogsTool((id, _) => Task.FromResult<string?>(id == "s2" ? "log line\n" : null));

        var found = Data(await tool.InvokeAsync(JsonValue.Parse("""{"id":"s2"}"""), Ctx, default));
        Assert.Equal("s2", ((JsonString)found["id"]!).Value);
        Assert.Equal("log line\n", ((JsonString)found["logs"]!).Value);

        var missing = await tool.InvokeAsync(JsonValue.Parse("""{"id":"nope"}"""), Ctx, default);
        Assert.IsType<UnknownTarget>(Assert.IsType<FailedResult>(missing).Reason);
    }

    [Fact]
    public async Task GetHeadlessJobLogs_MissingId_InvalidArgument()
    {
        var tool = new GetHeadlessJobLogsTool((_, _) => Task.FromResult<string?>(null));
        var result = await tool.InvokeAsync(JsonValue.Parse("{}"), Ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
    }

    [Fact]
    public async Task GetHeadlessJobEvents_FoundAndMissing()
    {
        var tool = new GetHeadlessJobEventsTool((id, _) => Task.FromResult<string?>(id == "s2" ? "Scheduled" : null));

        var found = Data(await tool.InvokeAsync(JsonValue.Parse("""{"id":"s2"}"""), Ctx, default));
        Assert.Equal("s2", ((JsonString)found["id"]!).Value);
        Assert.Equal("Scheduled", ((JsonString)found["events"]!).Value);

        var missing = await tool.InvokeAsync(JsonValue.Parse("""{"id":"nope"}"""), Ctx, default);
        Assert.IsType<UnknownTarget>(Assert.IsType<FailedResult>(missing).Reason);
    }
}
