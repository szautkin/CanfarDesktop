using CanfarDesktop.Services.Notebook;

namespace CanfarDesktop.Mcp.Tools.Write;

// ─────────────────────────────────────────────────────────────────────────────
// Native Jupyter notebook MCP tools. Reads return a snapshot; every mutation goes
// through ONE injected NotebookCommand applier (dispatched on the UI thread to the
// active notebook tab's view model) and returns the resulting NotebookState. All
// mutations are ViewState (live) — the notebook host is the user's editor, not a
// proposal target. open/create lazily create the notebook host + switch to it.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Shared JSON-Schema fragment + prose: the optional notebook selector every notebook tool
/// accepts, so the wording stays identical across the surface.</summary>
internal static class NbSel
{
    public const string Prop =
        "\"notebook\":{\"type\":\"string\",\"description\":\"Optional: target a specific open notebook by " +
        "its id (from list_open_notebooks) or file path; omit to target the active notebook\"}";
    public const string Note =
        " Targets the active notebook, or the open notebook named by `notebook` (id from list_open_notebooks, " +
        "or path) — targeting a non-active notebook does not switch the user's view.";
}

/// <summary><c>list_open_notebooks</c> — the currently-open notebook tabs (id, title, path, active, dirty, kernel).</summary>
public sealed class ListOpenNotebooksTool : JsonReadTool<ListOpenNotebooksTool.Args, ListOpenNotebooksTool.Output>
{
    private readonly Func<Task<IReadOnlyList<OpenNotebookInfo>>> _list;
    public ListOpenNotebooksTool(Func<Task<IReadOnlyList<OpenNotebookInfo>>> list) => _list = list;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "list_open_notebooks",
        "List the notebook tabs currently OPEN in the editor: each notebook's id, title, file path, whether " +
        "it's the active tab, dirty flag, cell count, and kernel state. Pass an id (or path) as the `notebook` " +
        "argument to get_notebook / edit_cell / run_cell / etc. to target a specific open notebook without " +
        "switching the user's active tab. (list_notebooks is different — that's the recently-opened history.)",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override async Task<Output> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var items = await _list();
        return new Output(items.Count, items);
    }

    public sealed record Args { }
    public sealed record Output(int Count, IReadOnlyList<OpenNotebookInfo> Notebooks);
}

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
    private readonly Func<string?, Task<NotebookState?>> _get;
    public GetNotebookTool(Func<string?, Task<NotebookState?>> get) => _get = get;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_notebook",
        "Read a notebook tab: notebookId, title, file path, dirty flag, kernel state, and the list of cells " +
        "(index, type, source, execution count, output count). Returns null if no notebook is open. Use " +
        "get_cell_output for a cell's outputs." + NbSel.Note,
        $$"""{"type":"object","properties":{{{NbSel.Prop}}},"additionalProperties":false}""");

    protected override Task<NotebookState?> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
        => _get(args.Notebook);

    public sealed record Args { public string? Notebook { get; init; } }
}

/// <summary><c>get_cell_output</c> — the outputs (text/error/flags) of a code cell in the active notebook.</summary>
public sealed class GetCellOutputTool : JsonReadTool<GetCellOutputTool.Args, NotebookCellOutputs?>
{
    private readonly Func<int, string?, Task<NotebookCellOutputs?>> _get;
    public GetCellOutputTool(Func<int, string?, Task<NotebookCellOutputs?>> get) => _get = get;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_cell_output",
        "Read the outputs of a cell (by 0-based index): each output's type, text, and error/image/html flags " +
        "(binary image data is flagged, not returned). Returns null if no notebook is open or the index is out " +
        "of range." + NbSel.Note,
        $$"""{"type":"object","properties":{"index":{"type":"integer","minimum":0},{{NbSel.Prop}}},"required":["index"],"additionalProperties":false}""");

    protected override Task<NotebookCellOutputs?> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (args.Index is null) throw new McpToolException(new InvalidArgument("index is required"));
        if (args.Index < 0) throw new McpToolException(new InvalidArgument("index must be >= 0"));
        return _get(args.Index.Value, args.Notebook);
    }

    public sealed record Args { public int? Index { get; init; } public string? Notebook { get; init; } }
}

/// <summary><c>get_kernel_state</c> — a notebook's kernel status.</summary>
public sealed class GetKernelStateTool : JsonReadTool<GetKernelStateTool.Args, NotebookKernelInfo>
{
    private readonly Func<string?, Task<NotebookKernelInfo>> _get;
    public GetKernelStateTool(Func<string?, Task<NotebookKernelInfo>> get) => _get = get;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_kernel_state",
        "Read a notebook's kernel status (Dead / Starting / Idle / Busy / Error) + kernel name. " +
        "Lighter than get_notebook for polling while a cell runs." + NbSel.Note,
        $$"""{"type":"object","properties":{{{NbSel.Prop}}},"additionalProperties":false}""");

    protected override Task<NotebookKernelInfo> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
        => _get(args.Notebook);

    public sealed record Args { public string? Notebook { get; init; } }
}

/// <summary>Base for the single-applier notebook mutation tools (each returns the resulting NotebookState).</summary>
public abstract class NotebookMutationTool<TArgs> : JsonReadTool<TArgs, NotebookState?> where TArgs : class, new()
{
    private readonly Func<NotebookCommand, Task<NotebookState?>> _apply;
    protected NotebookMutationTool(Func<NotebookCommand, Task<NotebookState?>> apply) => _apply = apply;
    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    /// <summary>Apply the command; a null result means no notebook is open, surfaced as TargetNotResolved so
    /// the agent gets an actionable "open_notebook / create_notebook first" instead of a bare null.</summary>
    protected async Task<NotebookState?> Apply(NotebookCommand cmd)
        => await _apply(cmd)
           ?? throw new McpToolException(new TargetNotResolved("no notebook is open — use open_notebook or create_notebook first"));
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
        "Save a notebook. With no path it saves to the current file (fails for an unsaved notebook — " +
        "pass a full .ipynb path to save-as)." + NbSel.Note,
        $$"""{"type":"object","properties":{"path":{"type":"string","description":"Optional full .ipynb path to save-as"},{{NbSel.Prop}}},"required":[],"additionalProperties":false}""");

    protected override Task<NotebookState?> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
        => Apply(new NotebookCommand(NotebookOp.Save, Path: string.IsNullOrWhiteSpace(args.Path) ? null : args.Path!.Trim(), Notebook: args.Notebook));

    public sealed record Args { public string? Path { get; init; } public string? Notebook { get; init; } }
}

/// <summary><c>edit_cell</c> — replace the source text of a cell.</summary>
public sealed class EditCellTool : NotebookMutationTool<EditCellTool.Args>
{
    public EditCellTool(Func<NotebookCommand, Task<NotebookState?>> apply) : base(apply) { }

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "edit_cell",
        "Replace the source text of the cell at a 0-based index." + NbSel.Note,
        $$"""{"type":"object","properties":{"index":{"type":"integer","minimum":0},"source":{"type":"string"},{{NbSel.Prop}}},"required":["index","source"],"additionalProperties":false}""");

    protected override Task<NotebookState?> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (args.Index is null) throw new McpToolException(new InvalidArgument("index is required"));
        if (args.Index < 0) throw new McpToolException(new InvalidArgument("index must be >= 0"));
        if (args.Source is null) throw new McpToolException(new InvalidArgument("source is required"));
        return Apply(new NotebookCommand(NotebookOp.EditCell, Index: args.Index, Source: args.Source, Notebook: args.Notebook));
    }

    public sealed record Args { public int? Index { get; init; } public string? Source { get; init; } public string? Notebook { get; init; } }
}

/// <summary><c>add_cell</c> — insert a new cell at an index (default: append).</summary>
public sealed class AddCellTool : NotebookMutationTool<AddCellTool.Args>
{
    public AddCellTool(Func<NotebookCommand, Task<NotebookState?>> apply) : base(apply) { }

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "add_cell",
        "Insert a new cell. type is 'code' (default) or 'markdown'; index is the 0-based position to insert " +
        "at (default: append at the end). Optionally set its source." + NbSel.Note,
        $$"""{"type":"object","properties":{"index":{"type":"integer","minimum":0},"type":{"type":"string","enum":["code","markdown"]},"source":{"type":"string"},{{NbSel.Prop}}},"required":[],"additionalProperties":false}""");

    protected override Task<NotebookState?> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
        => Apply(new NotebookCommand(NotebookOp.AddCell, Index: args.Index, CellType: NormalizeType(args.Type), Source: args.Source, Notebook: args.Notebook));

    private static string NormalizeType(string? t)
        => string.Equals(t, "markdown", StringComparison.OrdinalIgnoreCase) ? "markdown" : "code";

    public sealed record Args { public int? Index { get; init; } public string? Type { get; init; } public string? Source { get; init; } public string? Notebook { get; init; } }
}

/// <summary><c>delete_cell</c> — delete the cell at an index.</summary>
public sealed class DeleteCellTool : NotebookMutationTool<DeleteCellTool.Args>
{
    public DeleteCellTool(Func<NotebookCommand, Task<NotebookState?>> apply) : base(apply) { }

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "delete_cell",
        "Delete the cell at a 0-based index (deleting the last cell leaves one empty code cell)." + NbSel.Note,
        $$"""{"type":"object","properties":{"index":{"type":"integer","minimum":0},{{NbSel.Prop}}},"required":["index"],"additionalProperties":false}""");

    protected override Task<NotebookState?> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (args.Index is null) throw new McpToolException(new InvalidArgument("index is required"));
        if (args.Index < 0) throw new McpToolException(new InvalidArgument("index must be >= 0"));
        return Apply(new NotebookCommand(NotebookOp.DeleteCell, Index: args.Index, Notebook: args.Notebook));
    }

    public sealed record Args { public int? Index { get; init; } public string? Notebook { get; init; } }
}

/// <summary><c>change_cell_type</c> — convert a cell between code and markdown.</summary>
public sealed class ChangeCellTypeTool : NotebookMutationTool<ChangeCellTypeTool.Args>
{
    public ChangeCellTypeTool(Func<NotebookCommand, Task<NotebookState?>> apply) : base(apply) { }

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "change_cell_type",
        "Change the type of the cell at a 0-based index to 'code' or 'markdown'." + NbSel.Note,
        $$"""{"type":"object","properties":{"index":{"type":"integer","minimum":0},"type":{"type":"string","enum":["code","markdown"]},{{NbSel.Prop}}},"required":["index","type"],"additionalProperties":false}""");

    protected override Task<NotebookState?> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (args.Index is null) throw new McpToolException(new InvalidArgument("index is required"));
        if (args.Index < 0) throw new McpToolException(new InvalidArgument("index must be >= 0"));
        var type = string.Equals(args.Type, "markdown", StringComparison.OrdinalIgnoreCase) ? "markdown"
            : string.Equals(args.Type, "code", StringComparison.OrdinalIgnoreCase) ? "code" : null;
        if (type is null) throw new McpToolException(new InvalidArgument("type must be 'code' or 'markdown'"));
        return Apply(new NotebookCommand(NotebookOp.ChangeCellType, Index: args.Index, CellType: type, Notebook: args.Notebook));
    }

    public sealed record Args { public int? Index { get; init; } public string? Type { get; init; } public string? Notebook { get; init; } }
}

/// <summary><c>move_cell</c> — move a cell from one index to another.</summary>
public sealed class MoveCellTool : NotebookMutationTool<MoveCellTool.Args>
{
    public MoveCellTool(Func<NotebookCommand, Task<NotebookState?>> apply) : base(apply) { }

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "move_cell",
        "Move the cell at 0-based index 'from' to position 'to'." + NbSel.Note,
        $$"""{"type":"object","properties":{"from":{"type":"integer","minimum":0},"to":{"type":"integer","minimum":0},{{NbSel.Prop}}},"required":["from","to"],"additionalProperties":false}""");

    protected override Task<NotebookState?> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (args.From is null || args.To is null) throw new McpToolException(new InvalidArgument("from and to are required"));
        if (args.From < 0 || args.To < 0) throw new McpToolException(new InvalidArgument("from and to must be >= 0"));
        return Apply(new NotebookCommand(NotebookOp.MoveCell, Index: args.From, ToIndex: args.To, Notebook: args.Notebook));
    }

    public sealed record Args { public int? From { get; init; } public int? To { get; init; } public string? Notebook { get; init; } }
}

/// <summary><c>run_cell</c> — execute the code cell at an index (auto-starts the kernel).</summary>
public sealed class RunCellTool : NotebookMutationTool<RunCellTool.Args>
{
    public RunCellTool(Func<NotebookCommand, Task<NotebookState?>> apply) : base(apply) { }

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "run_cell",
        "Execute the code cell at a 0-based index (starts the kernel if needed). Returns the updated notebook " +
        "state; read the cell's outputs with get_cell_output." + NbSel.Note,
        $$"""{"type":"object","properties":{"index":{"type":"integer","minimum":0},{{NbSel.Prop}}},"required":["index"],"additionalProperties":false}""");

    protected override Task<NotebookState?> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (args.Index is null) throw new McpToolException(new InvalidArgument("index is required"));
        if (args.Index < 0) throw new McpToolException(new InvalidArgument("index must be >= 0"));
        return Apply(new NotebookCommand(NotebookOp.RunCell, Index: args.Index, Notebook: args.Notebook));
    }

    public sealed record Args { public int? Index { get; init; } public string? Notebook { get; init; } }
}

/// <summary><c>run_all_cells</c> — execute every code cell in order.</summary>
public sealed class RunAllCellsTool : NotebookMutationTool<RunAllCellsTool.Args>
{
    public RunAllCellsTool(Func<NotebookCommand, Task<NotebookState?>> apply) : base(apply) { }

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "run_all_cells",
        "Execute every code cell in order (starts the kernel if needed; stops if the kernel dies)." + NbSel.Note,
        $$"""{"type":"object","properties":{{{NbSel.Prop}}},"additionalProperties":false}""");

    protected override Task<NotebookState?> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
        => Apply(new NotebookCommand(NotebookOp.RunAll, Notebook: args.Notebook));

    public sealed record Args { public string? Notebook { get; init; } }
}

/// <summary><c>clear_cell_outputs</c> — clear all outputs + execution counts.</summary>
public sealed class ClearCellOutputsTool : NotebookMutationTool<ClearCellOutputsTool.Args>
{
    public ClearCellOutputsTool(Func<NotebookCommand, Task<NotebookState?>> apply) : base(apply) { }

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "clear_cell_outputs",
        "Clear all cell outputs and execution counts." + NbSel.Note,
        $$"""{"type":"object","properties":{{{NbSel.Prop}}},"additionalProperties":false}""");

    protected override Task<NotebookState?> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
        => Apply(new NotebookCommand(NotebookOp.ClearOutputs, Notebook: args.Notebook));

    public sealed record Args { public string? Notebook { get; init; } }
}

/// <summary><c>start_kernel</c> — start the active notebook's kernel.</summary>
public sealed class StartKernelTool : NotebookMutationTool<StartKernelTool.Args>
{
    public StartKernelTool(Func<NotebookCommand, Task<NotebookState?>> apply) : base(apply) { }

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "start_kernel",
        "Start the Python kernel (running a cell also auto-starts it)." + NbSel.Note,
        $$"""{"type":"object","properties":{{{NbSel.Prop}}},"additionalProperties":false}""");

    protected override Task<NotebookState?> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
        => Apply(new NotebookCommand(NotebookOp.StartKernel, Notebook: args.Notebook));

    public sealed record Args { public string? Notebook { get; init; } }
}

/// <summary><c>interrupt_kernel</c> — interrupt the running kernel.</summary>
public sealed class InterruptKernelTool : NotebookMutationTool<InterruptKernelTool.Args>
{
    public InterruptKernelTool(Func<NotebookCommand, Task<NotebookState?>> apply) : base(apply) { }

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "interrupt_kernel",
        "Interrupt the kernel (stops a long-running cell)." + NbSel.Note,
        $$"""{"type":"object","properties":{{{NbSel.Prop}}},"additionalProperties":false}""");

    protected override Task<NotebookState?> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
        => Apply(new NotebookCommand(NotebookOp.InterruptKernel, Notebook: args.Notebook));

    public sealed record Args { public string? Notebook { get; init; } }
}

/// <summary><c>restart_kernel</c> — restart the kernel fresh (clears execution counts).</summary>
public sealed class RestartKernelTool : NotebookMutationTool<RestartKernelTool.Args>
{
    public RestartKernelTool(Func<NotebookCommand, Task<NotebookState?>> apply) : base(apply) { }

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "restart_kernel",
        "Restart the Python kernel from a clean state." + NbSel.Note,
        $$"""{"type":"object","properties":{{{NbSel.Prop}}},"additionalProperties":false}""");

    protected override Task<NotebookState?> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
        => Apply(new NotebookCommand(NotebookOp.RestartKernel, Notebook: args.Notebook));

    public sealed record Args { public string? Notebook { get; init; } }
}

/// <summary>
/// <c>create_analysis_notebook</c> — the search → notebook hand-off (SCI-10): open a ready-to-run
/// notebook for a downloaded observation, seeded with metadata + an astropy load (path + WCS) + a
/// template stub. ViewState (live); doesn't go through the NotebookCommand applier — it has its own
/// host action that resolves the observation, writes the .ipynb, and opens it.
/// </summary>
public sealed class CreateAnalysisNotebookTool : JsonReadTool<CreateAnalysisNotebookTool.Args, NotebookState?>
{
    private readonly Func<string, string, Task<NotebookState?>> _create;
    public CreateAnalysisNotebookTool(Func<string, string, Task<NotebookState?>> create) => _create = create;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "create_analysis_notebook",
        "Open a ready-to-run analysis notebook for a DOWNLOADED observation (by its local id from " +
        "list_downloaded_observations, or its publisher id). Seeds a metadata header + an astropy load " +
        "cell (the local file path + WCS) + a template stub. template: image (zscale quick-look, the " +
        "default) | photometry (aperture photometry with photutils) | cube (moment map + spectrum with " +
        "spectral-cube). Returns the notebook state, or null if the observation isn't in Research " +
        "(download_observation it first). The local kernel needs astropy (+ photutils/spectral-cube for " +
        "the template). Live-applied (no proposal).",
        """{"type":"object","properties":{"observationId":{"type":"string"},"template":{"type":"string","enum":["image","photometry","cube"]}},"required":["observationId"],"additionalProperties":false}""");

    protected override Task<NotebookState?> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var id = (args.ObservationId ?? string.Empty).Trim();
        if (id.Length == 0) throw new McpToolException(new InvalidArgument("observationId is required"));
        return _create(id, (args.Template ?? "image").Trim());
    }

    public sealed record Args { public string? ObservationId { get; init; } public string? Template { get; init; } }
}
