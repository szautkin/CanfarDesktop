using System.Text;
using Xunit;
using CanfarDesktop.Models;
using CanfarDesktop.Models.ImageDiscovery;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Read;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

public class SessionImageToolTests
{
    private static readonly McpToolContext Ctx = McpToolContext.ForExternal("Claude/1.0", Guid.Empty);

    private static JsonObject Data(ToolResult r) => (JsonObject)JsonValue.Parse(Encoding.UTF8.GetString(Assert.IsType<DataResult>(r).Json));

    [Fact]
    public async Task ListSessions_ReturnsSummaries()
    {
        var tool = new ListSessionsTool(() => Task.FromResult<IReadOnlyList<Session>>(new List<Session>
        {
            new() { Id = "s1", SessionName = "nb", SessionType = "notebook", Status = "Running" },
        }));
        var data = Data(await tool.InvokeAsync(JsonValue.Null, Ctx, default));
        Assert.Equal(1, ((JsonInt)data["count"]!).Value);
        var s = (JsonObject)((JsonArray)data["sessions"]!).Items[0];
        Assert.Equal("notebook", ((JsonString)s["type"]!).Value);
        Assert.Equal("Running", ((JsonString)s["status"]!).Value);
    }

    [Fact]
    public async Task GetSession_FoundAndMissing()
    {
        var tool = new GetSessionTool(id => Task.FromResult<Session?>(id == "s1" ? new Session { Id = "s1", SessionName = "nb" } : null));

        var found = Data(await tool.InvokeAsync(JsonValue.Parse("""{"id":"s1"}"""), Ctx, default));
        Assert.Equal("nb", ((JsonString)found["name"]!).Value);

        var missing = await tool.InvokeAsync(JsonValue.Parse("""{"id":"nope"}"""), Ctx, default);
        Assert.IsType<UnknownTarget>(Assert.IsType<FailedResult>(missing).Reason);
    }

    [Fact]
    public async Task ListSessionTypes_IncludesNotebookAndHeadless()
    {
        var data = Data(await new ListSessionTypesTool().InvokeAsync(JsonValue.Null, Ctx, default));
        var types = ((JsonArray)data["types"]!).Items.Select(t => ((JsonString)t).Value).ToList();
        Assert.Contains("notebook", types);
        Assert.Contains("headless", types);
    }

    [Fact]
    public async Task FindImagesWithPackages_BuildsQueryAndReturnsIds()
    {
        PackageQuery? captured = null;
        var tool = new FindImagesWithPackagesTool(q => { captured = q; return new[] { "images.canfar.net/skaha/astroml:24.07" }; });

        var data = Data(await tool.InvokeAsync(JsonValue.Parse("""{"python":["numpy","astropy"]}"""), Ctx, default));

        Assert.Equal(new[] { "astropy", "numpy" }, captured!.Python.OrderBy(x => x)); // sorted
        Assert.Equal(1, ((JsonInt)data["count"]!).Value);
        Assert.Equal("images.canfar.net/skaha/astroml:24.07", ((JsonString)((JsonArray)data["imageIds"]!).Items[0]).Value);
    }

    [Fact]
    public async Task FindImagesWithPackages_EmptyQuery_InvalidArgument()
    {
        var tool = new FindImagesWithPackagesTool(_ => Array.Empty<string>());
        var result = await tool.InvokeAsync(JsonValue.Parse("{}"), Ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
    }
}
