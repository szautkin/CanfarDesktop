using CanfarDesktop.Services.Notebook;

namespace CanfarDesktop.Mcp.Tools.Write;

// ─────────────────────────────────────────────────────────────────────────────
// Native Jupyter notebook MCP tools. Reads return a snapshot; every mutation goes
// through ONE injected NotebookCommand applier (dispatched on the UI thread to the
// active notebook tab's view model) and returns the resulting NotebookState. All
// mutations are ViewState (live) — the notebook host is the user's editor, not a
// proposal target. open/create lazily create the notebook host + switch to it.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary><c>list_notebooks</c> — the user's recently-opened notebooks.</summary>
public sealed class ListNotebooksTool : JsonReadTool<ListNotebooksTool.Args, ListNotebooksTool.Output>
{
    private readonly Func<Task<IReadOnlyList<NotebookRef>>> _list;
    public ListNotebooksTool(Func<Task<IReadOnlyList<NotebookRef>>> list) => _list = list;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "list_notebooks",
        "List the user's recently-opened notebooks (path, name, last-opened time). Open one with open_notebook.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override async Task<Output> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var items = await _list();
        return new Output(items.Count, items);
    }

    public sealed record Args { }
    public sealed record Output(int Count, IReadOnlyList<NotebookRef> Notebooks);
}

/// <summary><c>get_notebook</c> — the active notebook tab: cells (index/type/source/exec count) + kernel state.</summary>
public sealed class GetNotebookTool : JsonReadTool<GetNotebookTool.Args, NotebookState?>
{
    private readonly Func<Task<NotebookState?>> _get;
    public GetNotebookTool(Func<Task<NotebookState?>> get) => _get = get;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_notebook",
        "Read the ACTIVE notebook tab: title, file path, dirty flag, kernel state, and the list of cells " +
        "(index, type, source, execution count, output count). Returns null if no notebook is open. Use " +
        "get_cell_output for a cell's outputs.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override Task<NotebookState?> HandleAsync(Args args, McpToolContext context, CancellationToken ct) => _get();

    public sealed record Args { }
}

/// <summary><c>get_cell_output</c> — the outputs (text/error/flags) of a code cell in the active notebook.</summary>
public sealed class GetCellOutputTool : JsonReadTool<GetCellOutputTool.Args, NotebookCellOutputs?>
{
    private readonly Func<int, Task<NotebookCellOutputs?>> _get;
    public GetCellOutputTool(Func<int, Task<NotebookCellOutputs?>> get) => _get = get;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_cell_output",
        "Read the outputs of a cell (by 0-based index) in the active notebook: each output's type, text, and " +
        "error/image/html flags (binary image data is flagged, not returned). Returns null if no notebook is " +
        "open or the index is out of range.",
        """{"type":"object","properties":{"index":{"type":"integer","minimum":0}},"required":["index"],"additionalProperties":false}""");

    protected override Task<NotebookCellOutputs?> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (args.Index is null) throw new McpToolException(new InvalidArgument("index is required"));
        if (args.Index < 0) throw new McpToolException(new InvalidArgument("index must be >= 0"));
        return _get(args.Index.Value);
    }

    public sealed record Args { public int? Index { get; init; } }
}

/// <summary><c>get_kernel_state</c> — the active notebook's kernel status.</summary>
public sealed class GetKernelStateTool : JsonReadTool<GetKernelStateTool.Args, NotebookKernelInfo>
{
    private readonly Func<Task<NotebookKernelInfo>> _get;
    public GetKernelStateTool(Func<Task<NotebookKernelInfo>> get) => _get = get;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_kernel_state",
        "Read the active notebook's kernel status (Dead / Starting / Idle / Busy / Error) + kernel name. " +
        "Lighter than get_notebook for polling while a cell runs.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override Task<NotebookKernelInfo> HandleAsync(Args args, McpToolContext context, CancellationToken ct) => _get();

    public sealed record Args { }
}

/// <summary>Base for the single-applier notebook mutation tools (each returns the resulting NotebookState).</summary>
public abstract class NotebookMutationTool<TArgs> : JsonReadTool<TArgs, NotebookState?> where TArgs : class, new()
{
    protected readonly Func<NotebookCommand, Task<NotebookState?>> Apply;
    protected NotebookMutationTool(Func<NotebookCommand, Task<NotebookState?>> apply) => Apply = apply;
    public override McpVerbClass VerbClass => McpVerbClass.ViewState;
}

/// <summary><c>open_notebook</c> — open a .ipynb/.py/.md file in the notebook editor (creates the host + switches to it).</summary>
public sealed class OpenNotebookTool : NotebookMutationTool<OpenNotebookTool.Args>
{
    public OpenNotebookTool(Func<NotebookCommand, Task<NotebookState?>> apply) : base(apply) { }

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "open_notebook",
        "Open a notebook or script file (.ipynb / .py / .md, full local path) in the notebook editor as a new " +
        "tab and switch to it. Returns the resulting notebook state. Live-applied.",
        """{"type":"object","properties":{"path":{"type":"string","description":"Full local path to a .ipynb/.py/.md file"}},"required":["path"],"additionalProperties":false}""");

    protected override Task<NotebookState?> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var path = (args.Path ?? string.Empty).Trim();
        if (path.Length == 0) throw new McpToolException(new InvalidArgument("path is required"));
        return Apply(new NotebookCommand(NotebookOp.Open, Path: path));
    }

    public sealed record Args { public string? Path { get; init; } }
}

/// <summary><c>create_notebook</c> — open a new empty notebook tab.</summary>
public sealed class CreateNotebookTool : NotebookMutationTool<CreateNotebookTool.Args>
{
    public CreateNotebookTool(Func<NotebookCommand, Task<NotebookState?>> apply) : base(apply) { }

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "create_notebook",
        "Open a new empty (Untitled) notebook tab and switch to it. Save it with save_notebook (provide a path). Live-applied.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override Task<NotebookState?> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
        => Apply(new NotebookCommand(NotebookOp.Create));

    public sealed record Args { }
}

/// <summary><c>save_notebook</c> — save the active notebook (to its path, or save-as to a new path).</summary>
public sealed class SaveNotebookTool : NotebookMutationTool<SaveNotebookTool.Args>
{
    public SaveNotebookTool(Func<NotebookCommand, Task<NotebookState?>> apply) : base(apply) { }

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "save_notebook",
        "Save the active notebook. With no path it saves to the current file (fails for an unsaved notebook — " +
        "pass a full .ipynb path to save-as). Live-applied.",
        """{"type":"object","properties":{"path":{"type":"string","description":"Optional full .ipynb path to save-as"}},"required":[],"additionalProperties":false}""");

    protected override Task<NotebookState?> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
        => Apply(new NotebookCommand(NotebookOp.Save, Path: string.IsNullOrWhiteSpace(args.Path) ? null : args.Path!.Trim()));

    public sealed record Args { public string? Path { get; init; } }
}

/// <summary><c>edit_cell</c> — replace the source text of a cell.</summary>
public sealed class EditCellTool : NotebookMutationTool<EditCellTool.Args>
{
    public EditCellTool(Func<NotebookCommand, Task<NotebookState?>> apply) : base(apply) { }

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "edit_cell",
        "Replace the source text of the cell at a 0-based index in the active notebook. Live-applied.",
        """{"type":"object","properties":{"index":{"type":"integer","minimum":0},"source":{"type":"string"}},"required":["index","source"],"additionalProperties":false}""");

    protected override Task<NotebookState?> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (args.Index is null) throw new McpToolException(new InvalidArgument("index is required"));
        if (args.Index < 0) throw new McpToolException(new InvalidArgument("index must be >= 0"));
        if (args.Source is null) throw new McpToolException(new InvalidArgument("source is required"));
        return Apply(new NotebookCommand(NotebookOp.EditCell, Index: args.Index, Source: args.Source));
    }

    public sealed record Args { public int? Index { get; init; } public string? Source { get; init; } }
}

/// <summary><c>add_cell</c> — insert a new cell at an index (default: append).</summary>
public sealed class AddCellTool : NotebookMutationTool<AddCellTool.Args>
{
    public AddCellTool(Func<NotebookCommand, Task<NotebookState?>> apply) : base(apply) { }

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "add_cell",
        "Insert a new cell into the active notebook. type is 'code' (default) or 'markdown'; index is the " +
        "0-based position to insert at (default: append at the end). Optionally set its source. Live-applied.",
        """{"type":"object","properties":{"index":{"type":"integer","minimum":0},"type":{"type":"string","enum":["code","markdown"]},"source":{"type":"string"}},"required":[],"additionalProperties":false}""");

    protected override Task<NotebookState?> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
        => Apply(new NotebookCommand(NotebookOp.AddCell, Index: args.Index, CellType: NormalizeType(args.Type), Source: args.Source));

    private static string NormalizeType(string? t)
        => string.Equals(t, "markdown", StringComparison.OrdinalIgnoreCase) ? "markdown" : "code";

    public sealed record Args { public int? Index { get; init; } public string? Type { get; init; } public string? Source { get; init; } }
}

/// <summary><c>delete_cell</c> — delete the cell at an index.</summary>
public sealed class DeleteCellTool : NotebookMutationTool<DeleteCellTool.Args>
{
    public DeleteCellTool(Func<NotebookCommand, Task<NotebookState?>> apply) : base(apply) { }

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "delete_cell",
        "Delete the cell at a 0-based index in the active notebook (deleting the last cell leaves one empty code cell). Live-applied.",
        """{"type":"object","properties":{"index":{"type":"integer","minimum":0}},"required":["index"],"additionalProperties":false}""");

    protected override Task<NotebookState?> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (args.Index is null) throw new McpToolException(new InvalidArgument("index is required"));
        if (args.Index < 0) throw new McpToolException(new InvalidArgument("index must be >= 0"));
        return Apply(new NotebookCommand(NotebookOp.DeleteCell, Index: args.Index));
    }

    public sealed record Args { public int? Index { get; init; } }
}

/// <summary><c>change_cell_type</c> — convert a cell between code and markdown.</summary>
public sealed class ChangeCellTypeTool : NotebookMutationTool<ChangeCellTypeTool.Args>
{
    public ChangeCellTypeTool(Func<NotebookCommand, Task<NotebookState?>> apply) : base(apply) { }

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "change_cell_type",
        "Change the type of the cell at a 0-based index to 'code' or 'markdown' in the active notebook. Live-applied.",
        """{"type":"object","properties":{"index":{"type":"integer","minimum":0},"type":{"type":"string","enum":["code","markdown"]}},"required":["index","type"],"additionalProperties":false}""");

    protected override Task<NotebookState?> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (args.Index is null) throw new McpToolException(new InvalidArgument("index is required"));
        if (args.Index < 0) throw new McpToolException(new InvalidArgument("index must be >= 0"));
        var type = string.Equals(args.Type, "markdown", StringComparison.OrdinalIgnoreCase) ? "markdown"
            : string.Equals(args.Type, "code", StringComparison.OrdinalIgnoreCase) ? "code" : null;
        if (type is null) throw new McpToolException(new InvalidArgument("type must be 'code' or 'markdown'"));
        return Apply(new NotebookCommand(NotebookOp.ChangeCellType, Index: args.Index, CellType: type));
    }

    public sealed record Args { public int? Index { get; init; } public string? Type { get; init; } }
}

/// <summary><c>move_cell</c> — move a cell from one index to another.</summary>
public sealed class MoveCellTool : NotebookMutationTool<MoveCellTool.Args>
{
    public MoveCellTool(Func<NotebookCommand, Task<NotebookState?>> apply) : base(apply) { }

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "move_cell",
        "Move the cell at 0-based index 'from' to position 'to' in the active notebook. Live-applied.",
        """{"type":"object","properties":{"from":{"type":"integer","minimum":0},"to":{"type":"integer","minimum":0}},"required":["from","to"],"additionalProperties":false}""");

    protected override Task<NotebookState?> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (args.From is null || args.To is null) throw new McpToolException(new InvalidArgument("from and to are required"));
        if (args.From < 0 || args.To < 0) throw new McpToolException(new InvalidArgument("from and to must be >= 0"));
        return Apply(new NotebookCommand(NotebookOp.MoveCell, Index: args.From, ToIndex: args.To));
    }

    public sealed record Args { public int? From { get; init; } public int? To { get; init; } }
}

/// <summary><c>run_cell</c> — execute the code cell at an index (auto-starts the kernel).</summary>
public sealed class RunCellTool : NotebookMutationTool<RunCellTool.Args>
{
    public RunCellTool(Func<NotebookCommand, Task<NotebookState?>> apply) : base(apply) { }

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "run_cell",
        "Execute the code cell at a 0-based index in the active notebook (starts the kernel if needed). Returns " +
        "the updated notebook state; read the cell's outputs with get_cell_output. Live-applied.",
        """{"type":"object","properties":{"index":{"type":"integer","minimum":0}},"required":["index"],"additionalProperties":false}""");

    protected override Task<NotebookState?> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (args.Index is null) throw new McpToolException(new InvalidArgument("index is required"));
        if (args.Index < 0) throw new McpToolException(new InvalidArgument("index must be >= 0"));
        return Apply(new NotebookCommand(NotebookOp.RunCell, Index: args.Index));
    }

    public sealed record Args { public int? Index { get; init; } }
}

/// <summary><c>run_all_cells</c> — execute every code cell in order.</summary>
public sealed class RunAllCellsTool : NotebookMutationTool<RunAllCellsTool.Args>
{
    public RunAllCellsTool(Func<NotebookCommand, Task<NotebookState?>> apply) : base(apply) { }

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "run_all_cells",
        "Execute every code cell in the active notebook, in order (starts the kernel if needed; stops if the kernel dies). Live-applied.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override Task<NotebookState?> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
        => Apply(new NotebookCommand(NotebookOp.RunAll));

    public sealed record Args { }
}

/// <summary><c>clear_cell_outputs</c> — clear all outputs + execution counts.</summary>
public sealed class ClearCellOutputsTool : NotebookMutationTool<ClearCellOutputsTool.Args>
{
    public ClearCellOutputsTool(Func<NotebookCommand, Task<NotebookState?>> apply) : base(apply) { }

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "clear_cell_outputs",
        "Clear all cell outputs and execution counts in the active notebook. Live-applied.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override Task<NotebookState?> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
        => Apply(new NotebookCommand(NotebookOp.ClearOutputs));

    public sealed record Args { }
}

/// <summary><c>start_kernel</c> — start the active notebook's kernel.</summary>
public sealed class StartKernelTool : NotebookMutationTool<StartKernelTool.Args>
{
    public StartKernelTool(Func<NotebookCommand, Task<NotebookState?>> apply) : base(apply) { }

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "start_kernel",
        "Start the Python kernel for the active notebook (running a cell also auto-starts it). Live-applied.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override Task<NotebookState?> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
        => Apply(new NotebookCommand(NotebookOp.StartKernel));

    public sealed record Args { }
}

/// <summary><c>interrupt_kernel</c> — interrupt the running kernel.</summary>
public sealed class InterruptKernelTool : NotebookMutationTool<InterruptKernelTool.Args>
{
    public InterruptKernelTool(Func<NotebookCommand, Task<NotebookState?>> apply) : base(apply) { }

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "interrupt_kernel",
        "Interrupt the active notebook's kernel (stops a long-running cell). Live-applied.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override Task<NotebookState?> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
        => Apply(new NotebookCommand(NotebookOp.InterruptKernel));

    public sealed record Args { }
}

/// <summary><c>restart_kernel</c> — restart the kernel fresh (clears execution counts).</summary>
public sealed class RestartKernelTool : NotebookMutationTool<RestartKernelTool.Args>
{
    public RestartKernelTool(Func<NotebookCommand, Task<NotebookState?>> apply) : base(apply) { }

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "restart_kernel",
        "Restart the active notebook's Python kernel from a clean state. Live-applied.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override Task<NotebookState?> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
        => Apply(new NotebookCommand(NotebookOp.RestartKernel));

    public sealed record Args { }
}
