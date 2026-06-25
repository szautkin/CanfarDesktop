using CanfarDesktop.Models;

namespace CanfarDesktop.Mcp.Tools.Read;

/// <summary><c>get_storage_quota</c> — the user's VOSpace/ARC storage usage vs quota.</summary>
public sealed class GetStorageQuotaTool : JsonReadTool<EmptyArgs, GetStorageQuotaTool.Output>
{
    private readonly Func<CancellationToken, Task<StorageQuota?>> _quota;

    public GetStorageQuotaTool(Func<CancellationToken, Task<StorageQuota?>> quota) => _quota = quota;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_storage_quota",
        "Report the user's VOSpace/ARC storage quota: bytes used vs total, GB, and percent used.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override async Task<Output> HandleAsync(EmptyArgs args, McpToolContext context, CancellationToken ct)
    {
        var q = await _quota(ct);
        if (q is null)
            throw new McpToolException(new TargetNotResolved("storage quota is unavailable (sign in to CADC/CANFAR?)"));

        return new Output(q.QuotaBytes, q.UsedBytes, Math.Round(q.QuotaGB, 2), Math.Round(q.UsedGB, 2), Math.Round(q.UsagePercent, 1), q.LastModified);
    }

    public sealed record Output(long QuotaBytes, long UsedBytes, double QuotaGB, double UsedGB, double UsagePercent, string? LastModified);
}

/// <summary><c>get_platform_load</c> — current CANFAR Science Platform CPU/RAM headroom + instance counts.</summary>
public sealed class GetPlatformLoadTool : JsonReadTool<EmptyArgs, GetPlatformLoadTool.Output>
{
    private readonly Func<CancellationToken, Task<SkahaStatsResponse?>> _stats;

    public GetPlatformLoadTool(Func<CancellationToken, Task<SkahaStatsResponse?>> stats) => _stats = stats;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_platform_load",
        "Report the CANFAR Science Platform load: CPU cores + RAM requested vs available, and the number of " +
        "running session / desktop-app / headless instances. Useful before launching to gauge headroom.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override async Task<Output> HandleAsync(EmptyArgs args, McpToolContext context, CancellationToken ct)
    {
        var s = await _stats(ct);
        if (s is null)
            throw new McpToolException(new TargetNotResolved("platform stats are unavailable"));

        return new Output(
            s.Cores.RequestedCPUCores, s.Cores.CpuCoresAvailable,
            s.Ram.RequestedRAM, s.Ram.RamAvailable,
            s.Instances?.Session, s.Instances?.DesktopApp, s.Instances?.Headless, s.Instances?.Total);
    }

    public sealed record Output(
        double RequestedCpuCores, double CpuCoresAvailable,
        string RequestedRam, string RamAvailable,
        int? SessionInstances, int? DesktopAppInstances, int? HeadlessInstances, int? TotalInstances);
}
