namespace CanfarDesktop.Services.Export;

/// <summary>
/// A feature module with user data to export. Exporters produce Claude-friendly output: markdown for
/// human/LLM content, JSON for structured metadata, stable keys (publisherID) for cross-reference.
/// Export/ is a leaf — features depend on it, not vice-versa. 1-to-1 with the macOS ExportableModule.
/// </summary>
public interface IExportableModule
{
    /// <summary>Stable id, also the bundle subdirectory name (e.g. "research", "search").</summary>
    string ModuleId { get; }

    /// <summary>Human-readable name (e.g. "Research").</summary>
    string DisplayName { get; }

    Task<ExportModuleOutput> ExportAsync(ExportOptions options);
}

/// <summary>The payload a single module contributes to the bundle.</summary>
public class ExportModuleOutput
{
    /// <summary>Filename (relative to the module subdir) → JSON text.</summary>
    public Dictionary<string, string> JsonFiles { get; } = new();

    /// <summary>Filename (relative to the module subdir) → markdown text.</summary>
    public Dictionary<string, string> MarkdownFiles { get; } = new();

    /// <summary>Absolute paths of external files to copy in when <see cref="ExportOptions.IncludeFileCopies"/>.</summary>
    public List<string> AttachedFiles { get; } = new();

    /// <summary>Summary counts shown in the manifest (e.g. {"observations": 42, "notes": 12}).</summary>
    public Dictionary<string, int> ItemCounts { get; } = new();
}

/// <summary>User-configurable export behavior.</summary>
public class ExportOptions
{
    public bool IncludeFileCopies { get; init; }
    public bool IncludeNotes { get; init; } = true;
    public bool IncludeSearchHistory { get; init; } = true;
}
