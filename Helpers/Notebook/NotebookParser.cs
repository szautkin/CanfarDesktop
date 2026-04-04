namespace CanfarDesktop.Helpers.Notebook;

using System.Text.Json;
using System.Text.Json.Serialization;
using CanfarDesktop.Models.Notebook;

/// <summary>
/// Pure static parser for .ipynb files. No side effects, no I/O beyond the
/// string/stream it is given. Follows the ResultSorter pattern.
/// </summary>
public static class NotebookParser
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Parse a .ipynb JSON string into a NotebookDocument.
    /// Throws JsonException on malformed JSON.
    /// </summary>
    public static NotebookDocument Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var doc = JsonSerializer.Deserialize<NotebookDocument>(json, ReadOptions)
                  ?? throw new JsonException("Deserialized notebook was null.");

        NormalizeCells(doc);
        return doc;
    }

    /// <summary>
    /// Parse from a UTF-8 stream. Preferred for file I/O to avoid double-allocation.
    /// </summary>
    public static async Task<NotebookDocument> ParseAsync(Stream stream, CancellationToken ct = default)
    {
        var doc = await JsonSerializer.DeserializeAsync<NotebookDocument>(stream, ReadOptions, ct)
                  ?? throw new JsonException("Deserialized notebook was null.");

        NormalizeCells(doc);
        return doc;
    }

    /// <summary>
    /// Serialize a NotebookDocument back to .ipynb JSON string.
    /// </summary>
    public static string Serialize(NotebookDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        EnforceOutputRules(document);
        return JsonSerializer.Serialize(document, WriteOptions);
    }

    /// <summary>
    /// Serialize directly to a UTF-8 stream. Preferred for file save.
    /// </summary>
    public static async Task SerializeAsync(NotebookDocument document, Stream stream, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        EnforceOutputRules(document);
        await JsonSerializer.SerializeAsync(stream, document, WriteOptions, ct);
    }

    /// <summary>
    /// Create a new empty notebook with sensible defaults.
    /// </summary>
    public static NotebookDocument CreateEmpty(string kernelName = "python3", string displayName = "Python 3")
    {
        return new NotebookDocument
        {
            NbFormat = 4,
            NbFormatMinor = 5,
            Metadata = new NotebookMetadata
            {
                KernelSpec = new KernelSpec
                {
                    Name = kernelName,
                    DisplayName = displayName,
                    Language = "python"
                },
                LanguageInfo = new LanguageInfo
                {
                    Name = "python",
                    Version = "",
                    MimeType = "text/x-python",
                    FileExtension = ".py"
                }
            },
            Cells =
            [
                new NotebookCell
                {
                    CellType = "code",
                    Id = GenerateCellId(),
                    Source = [],
                    Outputs = [],
                    ExecutionCount = null
                }
            ]
        };
    }

    /// <summary>
    /// 8-character hex ID, matching Jupyter's cell ID format.
    /// </summary>
    internal static string GenerateCellId() => Guid.NewGuid().ToString("N")[..8];

    private static void NormalizeCells(NotebookDocument doc)
    {
        foreach (var cell in doc.Cells)
        {
            if (cell.CellType == "code")
                cell.Outputs ??= [];
            cell.Id ??= GenerateCellId();
        }
    }

    private static void EnforceOutputRules(NotebookDocument document)
    {
        foreach (var cell in document.Cells)
        {
            if (cell.CellType != "code")
            {
                cell.Outputs = null;
                cell.ExecutionCount = null;
            }
        }
    }
}
