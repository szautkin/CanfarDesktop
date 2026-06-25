using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Read;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

public class ServiceHealthToolTests
{
    private static readonly McpToolContext Ctx = McpToolContext.ForExternal("c1", Guid.Empty);

    [Fact]
    public async Task SummarizesReachability()
    {
        var entries = new IReadOnlyList<ServiceHealthEntry>[]
        {
            new[]
            {
                new ServiceHealthEntry("TAP", "https://a", true, 200, 42, null),
                new ServiceHealthEntry("Skaha", "https://b", false, null, 5000, "TaskCanceledException"),
                new ServiceHealthEntry("Storage", "https://c", true, 401, 88, null),
            },
        }[0];

        var tool = new GetServiceHealthTool(() => Task.FromResult(entries));
        var doc = JsonDocument.Parse(Assert.IsType<DataResult>(await tool.InvokeAsync(JsonValue.Null, Ctx, default)).Json).RootElement;

        Assert.Equal(3, doc.GetProperty("count").GetInt32());
        Assert.Equal(2, doc.GetProperty("reachableCount").GetInt32());
        Assert.False(doc.GetProperty("services")[1].GetProperty("reachable").GetBoolean());
    }
}
