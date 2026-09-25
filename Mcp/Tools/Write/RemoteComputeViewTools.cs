using CanfarDesktop.Models.AICompute;
using CanfarDesktop.Services.AICompute;

namespace CanfarDesktop.Mcp.Tools.Write;

// ─────────────────────────────────────────────────────────────────────────────
// The Remote Compute screen, for an agent. run_code, start_compute and friends act;
// these put things on the person's screen — a run to look at, code for them to run
// — and read back what is there, so everything a person can do on that screen an
// agent can do or show them. Configuring compute is deliberately not here: setting
// the image is the person's consent to code running on their account.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>One run in full, as the screen's detail tab shows it.</summary>
public sealed record ComputeRunDetail(
    string ExecutionId, string Author, string Language, string SubmittedAt, string Status,
    int? ExitCode, long? DurationMs, string? FinishedAt, int TimeoutSeconds, string Code)
{
    public static ComputeRunDetail From(ComputeRun r) => new(
        r.Id, r.Author.Name(), r.Language, r.SubmittedAt,
        r.State, r.ExitCode, r.DurationMs, r.FinishedAt, r.TimeoutSeconds, r.Code);
}

/// <summary>What is in the Run code tab — possibly code the person is still writing.</summary>
public sealed record ComputeSnippetView(string Language, int TimeoutSeconds, string Code);

/// <summary>What the Remote Compute screen shows.</summary>
/// <param name="Shown">Whether the screen is on screen now.</param>
/// <param name="Tab"><c>run</c> (a run's details) or <c>code</c> (the Run code tab).</param>
public sealed record ComputeScreenView(
    bool Shown, string State, string Tab, ComputeRunDetail? SelectedRun, ComputeSnippetView Snippet,
    string? Message = null)
{
    /// <summary>The answer when there is no screen to ask — no window yet, or none reachable.</summary>
    public static ComputeScreenView Unavailable(string message) => new(
        false, "unknown", "run", null,
        new ComputeSnippetView(RunCodeContract.DefaultLanguage, RunCodeContract.DefaultTimeoutSeconds, string.Empty),
        message);
}

/// <summary>What <c>set_compute_snippet</c> was asked to put in the box.</summary>
public sealed record ComputeSnippetRequest(string Code, string Language, int TimeoutSeconds);

/// <summary><c>get_compute_view</c> — what the Remote Compute screen shows right now.</summary>
public sealed class GetComputeViewTool : JsonReadTool<GetComputeViewTool.Args, ComputeScreenView>
{
    private readonly Func<Task<ComputeScreenView>> _view;
    public GetComputeViewTool(Func<Task<ComputeScreenView>> view) => _view = view;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_compute_view",
        "What the Remote Compute screen shows: whether it is on screen, the compute state, which tab is " +
        "open, the run selected in the list (with its full code), and what is in the Run code box — " +
        "which may be code the person is writing and has not run. Read-only.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override Task<ComputeScreenView> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
        => _view();

    public sealed record Args { }
}

/// <summary><c>show_compute_run</c> — put a run on the person's screen.</summary>
public sealed class ShowComputeRunTool : JsonReadTool<ShowComputeRunTool.Args, ComputeScreenView>
{
    private readonly Func<string?, Task<ComputeScreenView>> _show;
    public ShowComputeRunTool(Func<string?, Task<ComputeScreenView>> show) => _show = show;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "show_compute_run",
        "Open the Remote Compute screen with one run selected, so the person sees its code, output and " +
        "errors — the same as clicking it in the list. Omit executionId for the newest run. Returns the " +
        "run in full. Live-applied.",
        """{"type":"object","properties":{"executionId":{"type":"string","description":"From list_compute_runs or run_code; omit for the newest"}},"additionalProperties":false}""");

    protected override Task<ComputeScreenView> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
        => _show(string.IsNullOrWhiteSpace(args.ExecutionId) ? null : args.ExecutionId.Trim());

    public sealed record Args { public string? ExecutionId { get; init; } }
}

/// <summary>
/// <c>set_compute_snippet</c> — put code in the Run code box for the person to run.
///
/// <para>It fills the box and stops there. Running it is the person's press of Run — or run_code, when
/// the agent means to run it itself. The difference is whose run it is: this one is theirs.</para>
/// </summary>
public sealed class SetComputeSnippetTool : JsonReadTool<SetComputeSnippetTool.Args, ComputeScreenView>
{
    private readonly Func<ComputeSnippetRequest, Task<ComputeScreenView>> _set;
    public SetComputeSnippetTool(Func<ComputeSnippetRequest, Task<ComputeScreenView>> set) => _set = set;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "set_compute_snippet",
        "Put code in the Remote Compute screen's Run code box, with its language and timeout, and show " +
        "it — without running it. The person reads it and presses Run, and the run is theirs. To run " +
        "code yourself, use run_code instead. Replaces whatever was in the box. Live-applied.",
        """{"type":"object","properties":{"code":{"type":"string","minLength":1},"language":{"type":"string","enum":["python","bash"],"description":"Default python"},"timeoutSeconds":{"type":"integer","minimum":1,"maximum":900,"description":"Default 60"}},"required":["code"],"additionalProperties":false}""");

    protected override Task<ComputeScreenView> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var code = args.Code ?? string.Empty;
        if (code.Trim().Length == 0) throw new McpToolException(new InvalidArgument("code is required"));

        return _set(new ComputeSnippetRequest(
            code,
            RunCodeContract.NormalizeLanguage(args.Language),
            RunCodeContract.ClampTimeout(args.TimeoutSeconds ?? RunCodeContract.DefaultTimeoutSeconds)));
    }

    public sealed record Args
    {
        public string? Code { get; init; }
        public string? Language { get; init; }
        public int? TimeoutSeconds { get; init; }
    }
}

/// <summary>What came of opening Storage at a folder.</summary>
public sealed record StorageFolderShown(bool Shown, string Folder, string? Message = null);

/// <summary>
/// <c>show_storage_folder</c> — open the Storage screen at a folder in the person's home.
///
/// <para>list_vospace_path reads any folder; this is the other half, putting one on screen — what the
/// breadcrumb, a double-click and Remote Compute's "Open folder in Storage" do for a person.</para>
/// </summary>
public sealed class ShowStorageFolderTool : JsonReadTool<ShowStorageFolderTool.Args, StorageFolderShown>
{
    private readonly Func<string, Task<StorageFolderShown>> _show;
    public ShowStorageFolderTool(Func<string, Task<StorageFolderShown>> show) => _show = show;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "show_storage_folder",
        "Open the Storage screen at a folder in the person's own home, so they see it. Pass the folder " +
        "relative to their home (\".verbinal/exec\", \"data/run1\"), empty for the home itself, or a " +
        "full \"/<username>/…\" path. Other people's areas and projects are read with list_vospace_path, " +
        "not shown here. Live-applied.",
        """{"type":"object","properties":{"folder":{"type":"string","description":"Relative to the home, or /<username>/…"}},"additionalProperties":false}""");

    protected override Task<StorageFolderShown> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
        => _show(args.Folder ?? string.Empty);

    public sealed record Args { public string? Folder { get; init; } }
}
