using System.Text.Json;
using Xunit;
using CanfarDesktop.Models;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Read;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

public class GetSavedQueryToolTests
{
    private static readonly McpToolContext Ctx = McpToolContext.ForExternal("c1", Guid.Empty);
    private static JsonValue Args(string json) => JsonValue.Parse(json);

    private static readonly IReadOnlyList<SavedQuery> Saved = new[]
    {
        new SavedQuery { Name = "M31 cone", Adql = "SELECT 1", SavedAt = DateTime.UtcNow },
        new SavedQuery { Name = "bright stars", Adql = "SELECT 2", SavedAt = DateTime.UtcNow },
    };

    [Fact]
    public async Task ReturnsMatchingQueryByName()
    {
        var tool = new GetSavedQueryTool(() => Saved);
        var result = await tool.InvokeAsync(Args("""{"name":"M31 cone"}"""), Ctx, default);

        var doc = JsonDocument.Parse(Assert.IsType<DataResult>(result).Json).RootElement;
        Assert.Equal("M31 cone", doc.GetProperty("name").GetString());
        Assert.Equal("SELECT 1", doc.GetProperty("adql").GetString());
    }

    [Fact]
    public async Task UnknownName_UnknownTarget()
    {
        var tool = new GetSavedQueryTool(() => Saved);
        var result = await tool.InvokeAsync(Args("""{"name":"nope"}"""), Ctx, default);
        Assert.IsType<UnknownTarget>(Assert.IsType<FailedResult>(result).Reason);
    }

    [Fact]
    public async Task MissingName_InvalidArgument()
    {
        var tool = new GetSavedQueryTool(() => Saved);
        var result = await tool.InvokeAsync(Args("""{"name":"  "}"""), Ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
    }
}
