using System.Text.Json.Serialization;

namespace CanfarDesktop.Models;

// Resource options from Skaha /context endpoint
public class SessionContext
{
    [JsonPropertyName("cores")]
    public ResourceOptions Cores { get; set; } = new();
    [JsonPropertyName("memoryGB")]
    public ResourceOptions MemoryGB { get; set; } = new();
    [JsonPropertyName("gpus")]
    public GpuOptions Gpus { get; set; } = new();
}

public class ResourceOptions
{
    [JsonPropertyName("default")]
    public int Default { get; set; }
    [JsonPropertyName("options")]
    public int[] Options { get; set; } = [];
}

public class GpuOptions
{
    [JsonPropertyName("options")]
    public int[] Options { get; set; } = [];
}
