namespace CanfarDesktop.Models.Notebook;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Unified output type for all notebook cell outputs. The "output_type" discriminator
/// determines which fields are relevant: stream, display_data, execute_result, error.
/// Single class with nullable fields matches JSON reality and avoids custom converters.
/// </summary>
public class CellOutput
{
    [JsonPropertyName("output_type")]
    public string OutputType { get; set; } = string.Empty;

    // --- stream fields ---

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Text { get; set; }

    // --- display_data / execute_result fields ---

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, JsonElement>? Data { get; set; }

    [JsonPropertyName("metadata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, JsonElement>? OutputMetadata { get; set; }

    [JsonPropertyName("execution_count")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ExecutionCount { get; set; }

    // --- error fields ---

    [JsonPropertyName("ename")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Ename { get; set; }

    [JsonPropertyName("evalue")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Evalue { get; set; }

    [JsonPropertyName("traceback")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Traceback { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
