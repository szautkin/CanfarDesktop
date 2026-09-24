using CanfarDesktop.Helpers;
using CanfarDesktop.Mcp.Tools.Proposals;
using CanfarDesktop.Models.AICompute;
using CanfarDesktop.Services.AICompute;

namespace CanfarDesktop.Mcp.Tools.Write;

// ─────────────────────────────────────────────────────────────────────────────
// AI Compute tools (Feature B): run agent-authored code on a warm remote Skaha
// session via the /arc file-drop RPC. CANFAR compute is part of the platform's
// user experience (not billed usage), so — matching macOS — run_code and
// start_compute are SemanticWrite: they auto-apply under the user's auto-apply
// setting, and queue for review when it's off. stop_compute stays Destructive
// (it tears down a session mid-work) and always queues. run_code_output is a
// plain read. An empty configured compute image disables run_code/start_compute.
// ─────────────────────────────────────────────────────────────────────────────

public sealed record RunCodePayload(string Id, string Language, string Code, int TimeoutSeconds);
public sealed record StartComputePayload();
public sealed record StopComputePayload();

/// <summary><c>run_code</c> — run an agent-authored snippet on the warm compute session. SemanticWrite
/// (macOS parity): CANFAR compute is part of the platform's user experience, not billed usage, so with
/// auto-apply ON this runs without a per-call click; with it OFF it still queues for review.</summary>
public sealed class RunCodeTool : JsonWriteTool<RunCodeTool.Args>
{
    private readonly Func<AIComputeSettings> _settings;
    public RunCodeTool(Func<AIComputeSettings> settings) => _settings = settings;

    public override McpVerbClass VerbClass => McpVerbClass.SemanticWrite;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "run_code",
        "Run a short Python or Bash snippet on a warm remote CANFAR compute session (launched/reused " +
        "automatically on the user's account). Auto-applies when the user has auto-apply on; otherwise " +
        "queues for their approval. Returns immediately with an execution_id; fetch the result with " +
        "run_code_output(execution_id). Requires an AI compute image set in Settings.",
        """{"type":"object","properties":{"code":{"type":"string","minLength":1,"description":"The snippet to run"},"language":{"type":"string","enum":["python","bash"],"description":"Default python"},"timeoutSeconds":{"type":"integer","minimum":1,"maximum":900,"description":"Per-run timeout (default 60)"}},"required":["code"],"additionalProperties":false}""");

    protected override Task<ProposalPlan> PlanAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var code = args.Code ?? string.Empty;
        if (code.Trim().Length == 0) throw new McpToolException(new InvalidArgument("code is required"));

        var s = _settings();
        if (!s.IsEnabled)
            throw new McpToolException(new InvalidArgument(
                "run_code is not set up: it needs a compute image in Settings ▸ AI compute. The Remote Compute " +
                "screen (navigate_to remoteCompute) walks the person through it — point them there, or use " +
                "launch_headless_job instead."));

        var language = RunCodeContract.NormalizeLanguage(args.Language);
        var timeout = RunCodeContract.ClampTimeout(args.TimeoutSeconds ?? RunCodeContract.DefaultTimeoutSeconds);
        var id = Guid.NewGuid().ToString("N");
        var (cores, ram) = (RunCodeContract.ClampCores(s.Cores), RunCodeContract.ClampRam(s.Ram));

        var summary =
            $"Run {language} on {RunCodeContract.SessionName} (image {s.Image}, {cores}c/{ram}g). " +
            $"execution_id {id}. Fetch output with run_code_output(executionId: \"{id}\").";
        return Task.FromResult(ProposalPlan.Encoding("run_code", summary, new RunCodePayload(id, language, code, timeout)));
    }

    public sealed record Args
    {
        public string? Code { get; init; }
        public string? Language { get; init; }
        public int? TimeoutSeconds { get; init; }
    }
}

/// <summary><c>run_code_output</c> — fetch the result of a previous run_code by execution_id. Read (live).</summary>
public sealed class RunCodeOutputTool : JsonReadTool<RunCodeOutputTool.Args, RunCodeOutputTool.Output>
{
    private readonly Func<string, CancellationToken, Task<RunCodeResult?>> _fetch;
    public RunCodeOutputTool(Func<string, CancellationToken, Task<RunCodeResult?>> fetch) => _fetch = fetch;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "run_code_output",
        "Fetch the result of a previous run_code by its execution_id. Returns ready=false while the code is " +
        "still running (poll again); when ready, returns status (ok/error/timeout), exit code, stdout, and " +
        "stderr. If several polls stay not-ready, (re)submit with run_code.",
        """{"type":"object","properties":{"executionId":{"type":"string","minLength":1}},"required":["executionId"],"additionalProperties":false}""");

    protected override async Task<Output> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var id = (args.ExecutionId ?? string.Empty).Trim();
        if (id.Length == 0) throw new McpToolException(new InvalidArgument("execution_id is required"));

        var result = await _fetch(id, ct);
        if (result is null)
            return new Output(false, id, null, null, null, null, null, null, null, null,
                "No result yet — the code may still be running (or the compute session is still starting). Poll again; if it stays not-ready, call run_code again.");

        return new Output(true, id, result.Status, result.ExitCode,
            result.DecodedStdout(), result.DecodedStderr(),
            result.DurationMs, result.Truncated, result.StartedAt, result.FinishedAt, null);
    }

    public sealed record Args { public string? ExecutionId { get; init; } }

    public sealed record Output(
        bool Ready, string ExecutionId, string? Status, int? ExitCode,
        string? Stdout, string? Stderr, long? DurationMs, bool? Truncated,
        string? StartedAt, string? FinishedAt, string? Note);
}

/// <summary><c>start_compute</c> — pre-warm the compute session at the configured size. SemanticWrite
/// (macOS parity — platform compute, not billed usage).</summary>
public sealed class StartComputeTool : JsonWriteTool<StartComputeTool.Args>
{
    private readonly Func<AIComputeSettings> _settings;
    public StartComputeTool(Func<AIComputeSettings> settings) => _settings = settings;

    public override McpVerbClass VerbClass => McpVerbClass.SemanticWrite;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "start_compute",
        "Pre-warm the remote compute session (at the size configured in Settings ▸ AI compute) so the next " +
        "run_code starts faster. Auto-applies when the user has auto-apply on; otherwise queues for approval. " +
        "Reusing an already-running session is a no-op. Requires an AI compute image set in Settings.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override Task<ProposalPlan> PlanAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var s = _settings();
        if (!s.IsEnabled)
            throw new McpToolException(new InvalidArgument(
                "start_compute is not set up: it needs a compute image in Settings ▸ AI compute. The Remote " +
                "Compute screen (navigate_to remoteCompute) walks the person through it."));

        var (cores, ram) = (RunCodeContract.ClampCores(s.Cores), RunCodeContract.ClampRam(s.Ram));
        var summary = $"Pre-warm {RunCodeContract.SessionName} (image {s.Image}, {cores}c/{ram}g).";
        return Task.FromResult(ProposalPlan.Encoding("start_compute", summary, new StartComputePayload()));
    }

    public sealed record Args { }
}

/// <summary><c>stop_compute</c> — propose stopping the warm compute session. Destructive (macOS parity:
/// stopping tears down a session mid-work, so it always queues for approval).</summary>
public sealed class StopComputeTool : JsonWriteTool<StopComputeTool.Args>
{
    public override McpVerbClass VerbClass => McpVerbClass.Destructive;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "stop_compute",
        "Propose stopping the warm remote compute session to free its cores. Queues for the user's approval. " +
        "Idempotent — a no-op if nothing is running. NOTE: this is not a cancel; a request already submitted " +
        "may re-run when compute is next started.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override Task<ProposalPlan> PlanAsync(Args args, McpToolContext context, CancellationToken ct)
        => Task.FromResult(ProposalPlan.Encoding("stop_compute", $"Stop {RunCodeContract.SessionName} (frees platform compute).", new StopComputePayload()));

    public sealed record Args { }
}

/// <summary>What <c>get_compute_state</c> answers: the compute as the Remote Compute screen shows it.</summary>
public sealed record ComputeStateView(
    string State, bool Configured, string? Image, int Cores, int Ram,
    string? SessionId, string? SessionStatus, string? StartedAt, int? UptimeMinutes, string? Note);

/// <summary>
/// <c>get_compute_state</c> — whether remote compute is set up, and whether its session is running.
///
/// <para>The read an agent needs before run_code, and the one the Remote Compute screen's status chip
/// shows a person: not set up, stopped, starting, running, stopping or failed.</para>
/// </summary>
public sealed class GetComputeStateTool : JsonReadTool<GetComputeStateTool.Args, ComputeStateView>
{
    private readonly Func<CancellationToken, Task<ComputeStateView>> _state;
    public GetComputeStateTool(Func<CancellationToken, Task<ComputeStateView>> state) => _state = state;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_compute_state",
        "Whether remote compute (run_code) is set up, and the state of its session on the user's CANFAR " +
        "account: notSetUp, stopped, starting, running, stopping or failed — with the image, the size it " +
        "launches at, and how long it has been up. The same status the Remote Compute screen shows.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override Task<ComputeStateView> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
        => _state(ct);

    public sealed record Args { }
}

/// <summary>One remembered run, as <c>list_compute_runs</c> reports it.</summary>
public sealed record ComputeRunView(
    string ExecutionId, string Author, string Language, string SubmittedAt, string Status,
    int? ExitCode, long? DurationMs, string? FinishedAt, string CodePreview, int CodeLength);

/// <summary>
/// <c>list_compute_runs</c> — the code sent to remote compute from this app, newest first.
///
/// <para>Every run, whoever sent it: an agent's run_code or the person's own from the Remote Compute
/// screen. The output itself is one run_code_output call away.</para>
/// </summary>
public sealed class ListComputeRunsTool : JsonReadTool<ListComputeRunsTool.Args, ListComputeRunsTool.Output>
{
    /// <summary>How much of each run's code is quoted; the rest is counted.</summary>
    public const int PreviewLength = 400;

    private readonly Func<IReadOnlyList<ComputeRun>> _runs;
    public ListComputeRunsTool(Func<IReadOnlyList<ComputeRun>> runs) => _runs = runs;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "list_compute_runs",
        "The code sent to remote compute from this app, newest first — by an agent through run_code or " +
        "by the person from the Remote Compute screen: who sent it, the language, when, and its status " +
        "(running while it is out; then ok, error, timeout, noResult or notSent), with the start of the " +
        "code. Fetch a run's output with run_code_output(executionId).",
        """{"type":"object","properties":{"limit":{"type":"integer","minimum":1,"maximum":50,"description":"How many (default 10)"}},"additionalProperties":false}""");

    protected override Task<Output> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var all = _runs();
        var shown = all.Take(Math.Clamp(args.Limit ?? 10, 1, 50)).Select(View).ToList();
        return Task.FromResult(new Output(all.Count, shown));
    }

    public static ComputeRunView View(ComputeRun r) => new(
        r.Id,
        r.Author.Name(),
        r.Language,
        r.SubmittedAt,
        r.State,
        r.ExitCode, r.DurationMs, r.FinishedAt,
        r.Code.Length <= PreviewLength ? r.Code : r.Code[..PreviewLength],
        r.Code.Length);

    public sealed record Args { public int? Limit { get; init; } }

    public sealed record Output(int Total, IReadOnlyList<ComputeRunView> Runs);
}

// ── Appliers ──

public sealed class RunCodeApplier : IProposalApplier
{
    private readonly Func<RunCodeRequest, Task> _submit;
    public RunCodeApplier(Func<RunCodeRequest, Task> submit) => _submit = submit;
    public string Kind => "run_code";
    public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
    {
        var p = ProposalPayload.Decode<RunCodePayload>(proposal);
        return _submit(new RunCodeRequest(p.Id, p.Language, p.Code, p.TimeoutSeconds));
    }
}

public sealed class StartComputeApplier : IProposalApplier
{
    private readonly Func<Task> _start;
    public StartComputeApplier(Func<Task> start) => _start = start;
    public string Kind => "start_compute";
    public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
    {
        ProposalPayload.Decode<StartComputePayload>(proposal); // validate envelope
        return _start();
    }
}

public sealed class StopComputeApplier : IProposalApplier
{
    private readonly Func<Task> _stop;
    public StopComputeApplier(Func<Task> stop) => _stop = stop;
    public string Kind => "stop_compute";
    public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
    {
        ProposalPayload.Decode<StopComputePayload>(proposal);
        return _stop();
    }
}
