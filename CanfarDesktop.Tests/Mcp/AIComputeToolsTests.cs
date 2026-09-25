using System.Text.Json;
using Xunit;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Proposals;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Mcp.Wire;
using CanfarDesktop.Models.AICompute;
using CanfarDesktop.Services.AICompute;

namespace CanfarDesktop.Tests.Mcp;

public class AIComputeToolsTests
{
    private static JsonValue Args(string json) => JsonValue.Parse(json);

    private static (McpToolContext ctx, InMemoryProposalStore store) Ctx()
    {
        var store = new InMemoryProposalStore();
        return (McpToolContext.ForExternal("c1", Guid.NewGuid(), store, new ProposalBudget()), store);
    }

    private static AIComputeSettings Enabled => new() { Image = "images.canfar.net/p/verbinal-compute:1", Cores = 2, Ram = 4 };

    // ── run_code (SemanticWrite — macOS parity) ──

    [Fact]
    public async Task RunCode_BuildsProposal_WithExecutionIdInSummary()
    {
        var (ctx, _) = Ctx();
        var result = await new RunCodeTool(() => Enabled).InvokeAsync(Args("""{"code":"print(1)"}"""), ctx, default);
        var proposed = Assert.IsType<ProposedResult>(result);
        var payload = JsonSerializer.Deserialize<RunCodePayload>(proposed.Proposal.Payload, McpJson.Options)!;
        Assert.Equal("print(1)", payload.Code);
        Assert.Equal("python", payload.Language);
        Assert.Equal(60, payload.TimeoutSeconds);
        Assert.False(string.IsNullOrEmpty(payload.Id));
        Assert.Contains(payload.Id, proposed.Proposal.Summary);   // the agent reads the id from the proposal
    }

    [Fact]
    public async Task RunCode_DisabledWhenNoImage_InvalidArgument()
    {
        var (ctx, store) = Ctx();
        var result = await new RunCodeTool(() => new AIComputeSettings()).InvokeAsync(Args("""{"code":"x"}"""), ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
        Assert.Empty(store.List());
    }

    [Fact]
    public async Task RunCode_NormalizesLanguage_AndClampsTimeout()
    {
        var (ctx, _) = Ctx();
        var result = await new RunCodeTool(() => Enabled).InvokeAsync(
            Args("""{"code":"echo hi","language":"BASH","timeoutSeconds":5000}"""), ctx, default);
        var payload = JsonSerializer.Deserialize<RunCodePayload>(Assert.IsType<ProposedResult>(result).Proposal.Payload, McpJson.Options)!;
        Assert.Equal("bash", payload.Language);
        Assert.Equal(900, payload.TimeoutSeconds); // clamped to max
    }

    [Fact]
    public void VerbClasses_MatchMacOS_RunAndStartAutoApply_StopAlwaysQueues()
    {
        // CANFAR compute is platform UX, not billed usage: run_code/start_compute are SemanticWrite
        // (auto-apply under the user's setting, macOS parity). stop_compute stays Destructive — it
        // tears down a session mid-work, so it must always queue.
        Assert.Equal(McpVerbClass.SemanticWrite, new RunCodeTool(() => Enabled).VerbClass);
        Assert.Equal(McpVerbClass.SemanticWrite, new StartComputeTool(() => Enabled).VerbClass);
        Assert.Equal(McpVerbClass.Destructive, new StopComputeTool().VerbClass);
    }

    [Fact]
    public async Task StartCompute_DisabledWhenNoImage_InvalidArgument()
    {
        var (ctx, _) = Ctx();
        var result = await new StartComputeTool(() => new AIComputeSettings()).InvokeAsync(Args("{}"), ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
    }

    // ── run_code_output (read) ──

    [Fact]
    public async Task RunCodeOutput_NotReady_ReturnsReadyFalseWithNote()
    {
        var ctx = McpToolContext.ForExternal("c1", Guid.Empty);
        var tool = new RunCodeOutputTool((_, _) => Task.FromResult<RunCodeResult?>(null));
        var doc = Json(await tool.InvokeAsync(Args("""{"executionId":"abc"}"""), ctx, default));
        Assert.False(doc.GetProperty("ready").GetBoolean());
        Assert.False(string.IsNullOrEmpty(doc.GetProperty("note").GetString()));
    }

    [Fact]
    public async Task RunCodeOutput_Ready_ReturnsDecodedResult()
    {
        var ctx = McpToolContext.ForExternal("c1", Guid.Empty);
        var res = new RunCodeResult("ok", 0, "hello", "utf8", null, null, 12, false, null, null);
        var tool = new RunCodeOutputTool((_, _) => Task.FromResult<RunCodeResult?>(res));
        var doc = Json(await tool.InvokeAsync(Args("""{"executionId":"abc"}"""), ctx, default));
        Assert.True(doc.GetProperty("ready").GetBoolean());
        Assert.Equal("ok", doc.GetProperty("status").GetString());
        Assert.Equal("hello", doc.GetProperty("stdout").GetString());
    }

    // ── the person's view: state and history ──

    /// <summary>An agent refused because nothing is set up is told where the person can set it up.</summary>
    [Fact]
    public async Task RunCode_NotSetUp_SendsTheAgentToRemoteCompute()
    {
        var (ctx, _) = Ctx();
        var result = await new RunCodeTool(() => new AIComputeSettings()).InvokeAsync(Args("""{"code":"x"}"""), ctx, default);
        var reason = Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
        Assert.Contains("remoteCompute", reason.Detail);
    }

    [Fact]
    public async Task GetComputeState_ReportsWhatItIsGiven()
    {
        var ctx = McpToolContext.ForExternal("c1", Guid.Empty);
        var state = new ComputeStateView("running", true, "img:1", 2, 4, "s1", "Running", "2026-09-24T11:00:00Z", 30, null);
        var doc = Json(await new GetComputeStateTool(_ => Task.FromResult(state)).InvokeAsync(Args("{}"), ctx, default));

        Assert.Equal("running", doc.GetProperty("state").GetString());
        Assert.Equal(30, doc.GetProperty("uptimeMinutes").GetInt32());
    }

    private static ComputeRun Run(string id, ComputeRunAuthor author, string? status, string code = "print(1)")
        => new(id, author, "python", code, 60, "2026-09-24T12:00:00Z") { Status = status };

    [Fact]
    public async Task ListComputeRuns_SaysWhoSentEachAndWhereItStands()
    {
        var ctx = McpToolContext.ForExternal("c1", Guid.Empty);
        var runs = new[] { Run("b", ComputeRunAuthor.User, null), Run("a", ComputeRunAuthor.Agent, "ok") };
        var doc = Json(await new ListComputeRunsTool(() => runs).InvokeAsync(Args("{}"), ctx, default));

        Assert.Equal(2, doc.GetProperty("total").GetInt32());
        var listed = doc.GetProperty("runs");
        Assert.Equal("user", listed[0].GetProperty("author").GetString());
        Assert.Equal("running", listed[0].GetProperty("status").GetString());   // still out
        Assert.Equal("agent", listed[1].GetProperty("author").GetString());
        Assert.Equal("ok", listed[1].GetProperty("status").GetString());
    }

    [Fact]
    public async Task ListComputeRuns_QuotesTheStartOfLongCodeAndCountsTheRest()
    {
        var ctx = McpToolContext.ForExternal("c1", Guid.Empty);
        var code = new string('x', ListComputeRunsTool.PreviewLength + 100);
        var doc = Json(await new ListComputeRunsTool(() => [Run("a", ComputeRunAuthor.Agent, "ok", code)])
            .InvokeAsync(Args("{}"), ctx, default));

        var run = doc.GetProperty("runs")[0];
        Assert.Equal(ListComputeRunsTool.PreviewLength, run.GetProperty("codePreview").GetString()!.Length);
        Assert.Equal(code.Length, run.GetProperty("codeLength").GetInt32());
    }

    [Fact]
    public async Task ListComputeRuns_HonoursTheLimit()
    {
        var ctx = McpToolContext.ForExternal("c1", Guid.Empty);
        var runs = Enumerable.Range(0, 20).Select(i => Run($"r{i}", ComputeRunAuthor.Agent, "ok")).ToArray();
        var doc = Json(await new ListComputeRunsTool(() => runs).InvokeAsync(Args("""{"limit":3}"""), ctx, default));

        Assert.Equal(20, doc.GetProperty("total").GetInt32());
        Assert.Equal(3, doc.GetProperty("runs").GetArrayLength());
    }

    // ── get_compute_state: the snapshot as an agent reads it ──

    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private static Session ComputeSession(string status) => new()
    {
        Id = "kedczixz", SessionName = RunCodeContract.SessionName, Status = status, StartedTime = "2026-09-24T02:23:42Z",
    };

    /// <summary>
    /// The Store test's case: a fresh install with no image set, and yesterday's compute session still
    /// running on the account. It read "not set up", with nothing about a session the agent could stop.
    /// </summary>
    [Fact]
    public void ASessionRunningWhereNothingIsSetUpIsReportedWithAWayToStopIt()
    {
        var view = ComputeStateView.From(
            new ComputeSnapshot(ComputeState.Running, ComputeSession("Running"), "", 1, 1, Configured: false), Now);

        Assert.Equal("running", view.State);
        Assert.False(view.Configured);
        Assert.Null(view.Image);
        Assert.Equal("kedczixz", view.SessionId);
        Assert.Equal(2016, view.UptimeMinutes);
        Assert.Contains("stop_compute", view.Note);
    }

    [Fact]
    public void NothingSetUpAndNothingRunningPointsToTheSetUp()
    {
        var view = ComputeStateView.From(new ComputeSnapshot(ComputeState.NotSetUp, null, "", 1, 1, Configured: false), Now);

        Assert.Equal("notSetUp", view.State);
        Assert.False(view.Configured);
        Assert.Null(view.SessionId);
        Assert.Contains("navigate_to remoteCompute", view.Note);
    }

    [Fact]
    public void SetUpAndStoppedSaysSoPlainly()
    {
        var view = ComputeStateView.From(
            new ComputeSnapshot(ComputeState.Stopped, null, "images.canfar.net/p/verbinal-compute:1", 2, 4, Configured: true), Now);

        Assert.Equal("stopped", view.State);
        Assert.True(view.Configured);
        Assert.Equal("images.canfar.net/p/verbinal-compute:1", view.Image);
        Assert.Null(view.Note);
    }

    // ── appliers ──

    [Fact]
    public async Task Appliers_DecodeAndInvoke()
    {
        RunCodeRequest? submitted = null;
        var started = false;
        var stopped = false;
        var run = new RunCodeApplier(r => { submitted = r; return Task.CompletedTask; });
        var start = new StartComputeApplier(() => { started = true; return Task.CompletedTask; });
        var stop = new StopComputeApplier(() => { stopped = true; return Task.CompletedTask; });

        await run.ApplyAsync(Proposal("run_code", new RunCodePayload("id7", "python", "print(1)", 30)));
        await start.ApplyAsync(Proposal("start_compute", new StartComputePayload()));
        await stop.ApplyAsync(Proposal("stop_compute", new StopComputePayload()));

        Assert.Equal("id7", submitted!.Id);
        Assert.Equal(30, submitted.TimeoutSeconds);
        Assert.True(started);
        Assert.True(stopped);
    }

    private static PendingProposal Proposal<T>(string kind, T payload)
        => PendingProposal.Create("t", kind, "s", JsonSerializer.SerializeToUtf8Bytes(payload, McpJson.Options), OperationOrigin.External("c1"));

    private static JsonElement Json(ToolResult result)
        => JsonDocument.Parse(Assert.IsType<DataResult>(result).Json).RootElement;
}
