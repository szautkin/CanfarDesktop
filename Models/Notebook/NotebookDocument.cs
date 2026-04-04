namespace CanfarDesktop.Models.Notebook;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Root of a Jupyter .ipynb file (nbformat 4.x).
/// Uses JsonExtensionData to preserve unknown top-level fields during round-trip.
/// </summary>
public class NotebookDocument
{
    [JsonPropertyName("nbformat")]
    public int NbFormat { get; set; } = 4;

    [JsonPropertyName("nbformat_minor")]
    public int NbFormatMinor { get; set; } = 5;

    [JsonPropertyName("metadata")]
    public NotebookMetadata Metadata { get; set; } = new();

    [JsonPropertyName("cells")]
    public List<NotebookCell> Cells { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public class NotebookMetadata
{
    [JsonPropertyName("kernelspec")]
    public KernelSpec? KernelSpec { get; set; }

    [JsonPropertyName("language_info")]
    public LanguageInfo? LanguageInfo { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public class KernelSpec
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "python3";

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = "Python 3";

    [JsonPropertyName("language")]
    public string Language { get; set; } = "python";

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public class LanguageInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "python";

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("mimetype")]
    public string? MimeType { get; set; }

    [JsonPropertyName("file_extension")]
    public string? FileExtension { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
