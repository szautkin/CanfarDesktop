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

/// <summary>A snapshot of the active notebook tab + its cells + kernel, returned to the MCP layer.</summary>
public sealed record NotebookState(
    bool Loaded,
    string Title,
    string? FilePath,
    string FileMode,        // Notebook | PythonScript | Markdown
    bool IsDirty,
    string KernelState,     // Dead | Starting | Idle | Busy | Error
    string KernelName,
    int SelectedIndex,
    int CellCount,
    IReadOnlyList<NotebookCellInfo> Cells);

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
    bool HasHtml);

/// <summary>The outputs of a single code cell, returned by get_cell_output.</summary>
public sealed record NotebookCellOutputs(int Index, string Type, int? ExecutionCount, IReadOnlyList<NotebookOutputInfo> Outputs);

/// <summary>The kernel status of the active notebook, returned by the kernel tools.</summary>
public sealed record NotebookKernelInfo(string State, string StatusText, string KernelName);

/// <summary>The mutating notebook operations, dispatched through a single applier on the UI thread.</summary>
public enum NotebookOp
{
    Open, Create, Save,
    EditCell, AddCell, DeleteCell, ChangeCellType, MoveCell, ClearOutputs,
    RunCell, RunAll, StartKernel, InterruptKernel, RestartKernel,
}

/// <summary>A single notebook mutation request (only the fields relevant to the op are set).</summary>
public sealed record NotebookCommand(
    NotebookOp Op,
    int? Index = null,
    int? ToIndex = null,
    string? Source = null,
    string? CellType = null,
    string? Path = null);
