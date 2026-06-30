using System.Text.Json;
using Xunit;
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

    // ── run_code (Destructive write) ──

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
        Assert.Contains("PAID", proposed.Proposal.Summary);       // cost is explicit
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
    public void VerbClasses_ComputeWritesAreDestructive_SoTheyNeverAutoApply()
    {
        Assert.Equal(McpVerbClass.Destructive, new RunCodeTool(() => Enabled).VerbClass);
        Assert.Equal(McpVerbClass.Destructive, new StartComputeTool(() => Enabled).VerbClass);
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
