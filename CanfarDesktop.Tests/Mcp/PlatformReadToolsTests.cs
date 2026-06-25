using System.Text;
using System.Text.Json;
using Xunit;
using CanfarDesktop.Models;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Read;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

public class PlatformReadToolsTests
{
    private static readonly McpToolContext Ctx = McpToolContext.ForExternal("c1", Guid.Empty);

    private static JsonElement Json(ToolResult result)
        => JsonDocument.Parse(Assert.IsType<DataResult>(result).Json).RootElement;

    [Fact]
    public async Task StorageQuota_ReturnsUsageFields()
    {
        var quota = new StorageQuota { QuotaBytes = 10L * 1024 * 1024 * 1024, UsedBytes = 5L * 1024 * 1024 * 1024 };
        var tool = new GetStorageQuotaTool(_ => Task.FromResult<StorageQuota?>(quota));

        var doc = Json(await tool.InvokeAsync(JsonValue.Null, Ctx, default));
        Assert.Equal(10.0, doc.GetProperty("quotaGB").GetDouble());
        Assert.Equal(50.0, doc.GetProperty("usagePercent").GetDouble());
    }

    [Fact]
    public async Task StorageQuota_Null_TargetNotResolved()
    {
        var tool = new GetStorageQuotaTool(_ => Task.FromResult<StorageQuota?>(null));
        var result = await tool.InvokeAsync(JsonValue.Null, Ctx, default);
        Assert.IsType<TargetNotResolved>(Assert.IsType<FailedResult>(result).Reason);
    }

    [Fact]
    public async Task PlatformLoad_MapsCoresRamInstances()
    {
        var stats = new SkahaStatsResponse
        {
            Cores = new CoreStats { RequestedCPUCores = 120, CpuCoresAvailable = 40 },
            Ram = new RamStats { RequestedRAM = "480G", RamAvailable = "160G" },
            Instances = new InstanceStats { Session = 12, DesktopApp = 1, Headless = 3, Total = 16 },
        };
        var tool = new GetPlatformLoadTool(_ => Task.FromResult<SkahaStatsResponse?>(stats));

        var doc = Json(await tool.InvokeAsync(JsonValue.Null, Ctx, default));
        Assert.Equal(40.0, doc.GetProperty("cpuCoresAvailable").GetDouble());
        Assert.Equal("160G", doc.GetProperty("ramAvailable").GetString());
        Assert.Equal(16, doc.GetProperty("totalInstances").GetInt32());
    }

    [Fact]
    public async Task PlatformLoad_Null_TargetNotResolved()
    {
        var tool = new GetPlatformLoadTool(_ => Task.FromResult<SkahaStatsResponse?>(null));
        var result = await tool.InvokeAsync(JsonValue.Null, Ctx, default);
        Assert.IsType<TargetNotResolved>(Assert.IsType<FailedResult>(result).Reason);
    }
}
