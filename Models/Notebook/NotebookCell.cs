namespace CanfarDesktop.Models.Notebook;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// A single notebook cell. The "cell_type" discriminator drives UI template selection.
/// Source is always List of string (one per line) per nbformat spec.
/// </summary>
public class NotebookCell
{
    [JsonPropertyName("cell_type")]
    public string CellType { get; set; } = "code";

    [JsonPropertyName("source")]
    public List<string> Source { get; set; } = [];

    [JsonPropertyName("metadata")]
    public CellMetadata Metadata { get; set; } = new();

    [JsonPropertyName("outputs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<CellOutput>? Outputs { get; set; }

    [JsonPropertyName("execution_count")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ExecutionCount { get; set; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    [JsonIgnore]
    public string SourceText
    {
        get => string.Join("", Source);
        set => Source = SplitSourceLines(value);
    }

    /// <summary>
    /// Split text into nbformat source lines: each line ends with \n except possibly the last.
    /// </summary>
    internal static List<string> SplitSourceLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                lines.Add(text[start..(i + 1)]);
                start = i + 1;
            }
        }
        if (start < text.Length)
            lines.Add(text[start..]);
        return lines;
    }
}

public class CellMetadata
{
    [JsonPropertyName("collapsed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Collapsed { get; set; }

    [JsonPropertyName("scrolled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Scrolled { get; set; }

    [JsonPropertyName("tags")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Tags { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
