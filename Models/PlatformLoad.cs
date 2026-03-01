using System.Text.Json.Serialization;

namespace CanfarDesktop.Models;

/// <summary>
/// Maps to Skaha GET /v1/session?view=stats response
/// </summary>
public class SkahaStatsResponse
{
    [JsonPropertyName("cores")]
    public CoreStats Cores { get; set; } = new();

    [JsonPropertyName("ram")]
    public RamStats Ram { get; set; } = new();
}

public class CoreStats
{
    [JsonPropertyName("requestedCPUCores")]
    public double RequestedCPUCores { get; set; }

    [JsonPropertyName("cpuCoresAvailable")]
    public double CpuCoresAvailable { get; set; }
}

public class RamStats
{
    [JsonPropertyName("requestedRAM")]
    public string RequestedRAM { get; set; } = string.Empty;

    [JsonPropertyName("ramAvailable")]
    public string RamAvailable { get; set; } = string.Empty;
}
