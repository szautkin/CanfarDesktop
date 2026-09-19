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
                new ServiceHealthEntry("TAP", "https://a", true, true, 200, 42, null),
                new ServiceHealthEntry("Skaha", "https://b", false, false, null, 5000, "TaskCanceledException"),
                new ServiceHealthEntry("Storage", "https://c", true, true, 401, 88, null),
            },
        }[0];

        var tool = new GetServiceHealthTool(() => Task.FromResult(entries));
        var doc = JsonDocument.Parse(Assert.IsType<DataResult>(await tool.InvokeAsync(JsonValue.Null, Ctx, default)).Json).RootElement;

        Assert.Equal(3, doc.GetProperty("count").GetInt32());
        Assert.Equal(2, doc.GetProperty("reachableCount").GetInt32());
        Assert.False(doc.GetProperty("services")[1].GetProperty("reachable").GetBoolean());
    }

    [Fact]
    public async Task NotFoundService_IsReachableButNotHealthy()
    {
        // QA F3: CADC-auth answered 404 and was reported healthy. A 404/5xx entry must be excluded
        // from healthyCount while an auth-gated 401 still counts as a working service.
        IReadOnlyList<ServiceHealthEntry> entries = new[]
        {
            new ServiceHealthEntry("TAP", "https://a", true, true, 200, 42, null),
            new ServiceHealthEntry("Auth", "https://b", true, false, 404, 60, null),
            new ServiceHealthEntry("Storage", "https://c", true, false, 503, 70, null),
            new ServiceHealthEntry("Skaha", "https://d", true, true, 401, 80, null),
        };

        var tool = new GetServiceHealthTool(() => Task.FromResult(entries));
        var doc = JsonDocument.Parse(Assert.IsType<DataResult>(await tool.InvokeAsync(JsonValue.Null, Ctx, default)).Json).RootElement;

        Assert.Equal(4, doc.GetProperty("reachableCount").GetInt32());
        Assert.Equal(2, doc.GetProperty("healthyCount").GetInt32());
        Assert.False(doc.GetProperty("services")[1].GetProperty("ok").GetBoolean());
        Assert.Equal(404, doc.GetProperty("services")[1].GetProperty("statusCode").GetInt32());
        Assert.True(doc.GetProperty("services")[3].GetProperty("ok").GetBoolean());
    }
}
