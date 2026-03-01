using System.Text.Json.Serialization;

namespace CanfarDesktop.Models;

/// <summary>
/// Raw response from Skaha API (JSON deserialization target).
/// Represents the direct response structure returned by the Skaha session API.
/// </summary>
public class SkahaSessionResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("userid")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("runAsUID")]
    public string RunAsUID { get; set; } = string.Empty;

    [JsonPropertyName("runAsGID")]
    public string RunAsGID { get; set; } = string.Empty;

    [JsonPropertyName("supplementalGroups")]
    public int[]? SupplementalGroups { get; set; }

    [JsonPropertyName("image")]
    public string Image { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("startTime")]
    public string StartTime { get; set; } = string.Empty;

    [JsonPropertyName("expiryTime")]
    public string ExpiryTime { get; set; } = string.Empty;

    [JsonPropertyName("connectURL")]
    public string ConnectURL { get; set; } = string.Empty;

    [JsonPropertyName("requestedRAM")]
    public string? RequestedRAM { get; set; }

    [JsonPropertyName("requestedCPUCores")]
    public string? RequestedCPUCores { get; set; }

    [JsonPropertyName("requestedGPUCores")]
    public string? RequestedGPUCores { get; set; }

    [JsonPropertyName("ramInUse")]
    public string? RamInUse { get; set; }

    [JsonPropertyName("cpuCoresInUse")]
    public string? CpuCoresInUse { get; set; }

    [JsonPropertyName("isFixedResources")]
    public bool? IsFixedResources { get; set; }
}

/// <summary>
/// Normalized internal model used by ViewModels.
/// Represents a session in the application with processed and user-friendly properties.
/// </summary>
public class Session
{
    public string Id { get; set; } = string.Empty;

    public string SessionType { get; set; } = string.Empty;

    public string SessionName { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string ContainerImage { get; set; } = string.Empty;

    public string StartedTime { get; set; } = string.Empty;

    public string ExpiresTime { get; set; } = string.Empty;

    public string? MemoryUsage { get; set; }

    public string MemoryAllocated { get; set; } = string.Empty;

    public string? CpuUsage { get; set; }

    public string CpuAllocated { get; set; } = string.Empty;

    public string? GpuAllocated { get; set; }

    public bool IsFixedResources { get; set; }

    public string? ConnectUrl { get; set; }

    public string? RequestedRAM { get; set; }

    public string? RequestedCPU { get; set; }

    public string? RequestedGPU { get; set; }
}
