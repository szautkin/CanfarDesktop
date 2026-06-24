namespace CanfarDesktop.Models.Export;

/// <summary>Machine-readable bundle index written to the bundle root as <c>manifest.json</c>.</summary>
public class ExportManifest
{
    public string ExportVersion { get; set; } = "1.0";
    public string AppName { get; set; } = "Verbinal";
    public string AppVersion { get; set; } = "unknown";
    public DateTimeOffset ExportedAt { get; set; }
    public string HostName { get; set; } = string.Empty;
    public List<ExportManifestModule> Modules { get; set; } = new();
    public ExportClaudeHints ClaudeHints { get; set; } = new();
}

public class ExportManifestModule
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<string> Files { get; set; } = new();
    public Dictionary<string, int> ItemCounts { get; set; } = new();
}

/// <summary>Pointers that help an LLM ingest the bundle.</summary>
public class ExportClaudeHints
{
    /// <summary>Best markdown file for LLM ingestion (first .md across all modules).</summary>
    public string? PrimaryContext { get; set; }

    /// <summary>First JSON file across all modules, used as a schema reference.</summary>
    public string? MetadataSchema { get; set; }

    public string ReadMeFirst { get; set; } = "README.md";
}
