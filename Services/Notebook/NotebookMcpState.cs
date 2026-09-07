namespace CanfarDesktop.Services.Notebook;

/// <summary>A recently-opened notebook, for list_notebooks.</summary>
public sealed record NotebookRef(string Path, string Name, DateTime OpenedAt);

/// <summary>One cell in the active notebook (source capped for transport; full fidelity via the file).</summary>
public sealed record NotebookCellInfo(
    int Index,
    string Type,            // "code" | "markdown"
    string Source,
    bool SourceTruncated,
    int? ExecutionCount,
    int OutputCount);

/// <summary>A snapshot of a notebook tab + its cells + kernel, returned to the MCP layer.</summary>
public sealed record NotebookState(
    bool Loaded,
    string NotebookId,      // stable id for this open notebook (pass as the `notebook` selector)
    string Title,
    string? FilePath,
    string FileMode,        // Notebook | PythonScript | Markdown
    bool IsDirty,
    string KernelState,     // Dead | Starting | Idle | Busy | Error
    string KernelName,
    int SelectedIndex,
    int CellCount,
    IReadOnlyList<NotebookCellInfo> Cells);

/// <summary>One currently-open notebook tab, returned by list_open_notebooks.</summary>
public sealed record OpenNotebookInfo(
    string NotebookId,
    string Title,
    string? FilePath,
    bool IsActive,
    bool IsDirty,
    int CellCount,
    string KernelState);

/// <summary>One output of a code cell, returned by get_cell_output (binary image data omitted; flagged instead).</summary>
public sealed record NotebookOutputInfo(
    string OutputType,      // stream | execute_result | display_data | error
    string Text,
    bool TextTruncated,
    bool IsError,
    string ErrorName,
    string Traceback,
    bool TracebackTruncated,
    bool HasImage,
    bool HasHtml,
    /// <summary>
    /// Every MIME type this output carries, richest first.
    ///
    /// Without it a caller sees only the text and cannot tell a figure from a printed number: the
    /// plain-text fallback for a plot is the string "&lt;Figure size 640x480&gt;", which says nothing
    /// about the plot. This is what tells an agent that get_cell_image has something to fetch.
    /// </summary>
    IReadOnlyList<string> RichTypes);

/// <summary>The outputs of a single code cell, returned by get_cell_output.</summary>
public sealed record NotebookCellOutputs(int Index, string Type, int? ExecutionCount, IReadOnlyList<NotebookOutputInfo> Outputs);


/// <summary>
/// A cell's figure, as bytes ready to be handed back as MCP image content.
///
/// Not base64 inside a JSON string: an image returned as text is one the client has to be told how to
/// decode, and the protocol already has a content type for pictures.
/// </summary>
public sealed record NotebookCellImage(byte[] Data, string MimeType, int Index, string? Message = null)
{
    public static NotebookCellImage None(string message) => new([], "image/png", -1, message);
}

/// <summary>The kernel status of the active notebook, returned by the kernel tools.</summary>
public sealed record NotebookKernelInfo(string State, string StatusText, string KernelName);

/// <summary>The mutating notebook operations, dispatched through a single applier on the UI thread.</summary>
public enum NotebookOp
{
    Open, Create, Save,
    EditCell, AddCell, DeleteCell, ChangeCellType, MoveCell, ClearOutputs,
    RunCell, RunAll, StartKernel, InterruptKernel, RestartKernel,
}

/// <summary>A single notebook mutation request (only the fields relevant to the op are set).
/// <see cref="Notebook"/> optionally targets a specific open notebook by id or path; when null the
/// op targets the active tab.</summary>
public sealed record NotebookCommand(
    NotebookOp Op,
    int? Index = null,
    int? ToIndex = null,
    string? Source = null,
    string? CellType = null,
    string? Path = null,
    string? Notebook = null);
