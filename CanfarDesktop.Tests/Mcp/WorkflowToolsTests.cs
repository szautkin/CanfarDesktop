using System.Text.Json;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Proposals;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Mcp.Wire;
using CanfarDesktop.Services.Workflows;
using Xunit;

namespace CanfarDesktop.Tests.Mcp;

public class WorkflowToolsTests : IDisposable
{
    private static readonly McpToolContext ReadCtx = McpToolContext.ForExternal("c1", Guid.Empty);
    private static JsonValue Args(string json) => JsonValue.Parse(json);
    private static JsonElement Json(ToolResult r) => JsonDocument.Parse(Assert.IsType<DataResult>(r).Json).RootElement;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vb-wft-" + Guid.NewGuid().ToString("N"));
    private WorkflowStore NewStore(params (string, string)[] builtins) => new(_dir, () => builtins.ToList());

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public async Task ListAndGet_RoundTrip()
    {
        var store = NewStore(("demo", "# Demo\n> d\nTags: t\n- [ ] **A** — x\n      Tool: resolve_target\n      View: search\n"));
        var localId = store.SaveNew("mine", WorkflowFormat.Skeleton("mine"));

        var list = Json(await new ListWorkflowsTool(store).InvokeAsync(Args("{}"), ReadCtx, default));
        Assert.Equal(2, list.GetProperty("count").GetInt32());

        var got = Json(await new GetWorkflowTool(store).InvokeAsync(Args("""{"id":"builtin:demo"}"""), ReadCtx, default));
        Assert.Equal("Demo", got.GetProperty("title").GetString());
        var step = got.GetProperty("steps")[0];
        Assert.Equal("A", step.GetProperty("title").GetString());
        Assert.Equal("resolve_target", step.GetProperty("tools")[0].GetString());
        Assert.Equal("search", step.GetProperty("view").GetString());

        var missing = await new GetWorkflowTool(store).InvokeAsync(Args("""{"id":"local:nope"}"""), ReadCtx, default);
        Assert.IsType<UnknownTarget>(Assert.IsType<FailedResult>(missing).Reason);
        _ = localId;
    }

    [Fact]
    public async Task SaveWorkflow_ValidatesAndBuildsProposal()
    {
        var (ctx, _) = WriteCtx();
        var ok = await new SaveWorkflowTool().InvokeAsync(
            Args("""{"name":"P","text":"# P\n- [ ] **A** — x\n"}"""), ctx, default);
        var proposed = Assert.IsType<ProposedResult>(ok);
        var payload = JsonSerializer.Deserialize<SaveWorkflowPayload>(proposed.Proposal.Payload, McpJson.Options)!;
        Assert.Equal("local", payload.Location);
        Assert.Equal("save_workflow", proposed.Proposal.Kind);

        var noSteps = await new SaveWorkflowTool().InvokeAsync(Args("""{"name":"P","text":"# P\njust prose"}"""), ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(noSteps).Reason);
    }

    [Fact]
    public async Task SetStep_Use_Update_Delete_MapPayloads()
    {
        var (ctx, _) = WriteCtx();

        var step = Assert.IsType<ProposedResult>(await new SetWorkflowStepTool().InvokeAsync(
            Args("""{"id":"local:x","index":2}"""), ctx, default));
        var sp = JsonSerializer.Deserialize<SetWorkflowStepPayload>(step.Proposal.Payload, McpJson.Options)!;
        Assert.Equal(("local:x", 2, true), (sp.Id, sp.Index, sp.Done)); // done defaults true

        var use = Assert.IsType<ProposedResult>(await new UseWorkflowTool().InvokeAsync(
            Args("""{"id":"builtin:demo","name":"M31 run"}"""), ctx, default));
        var up = JsonSerializer.Deserialize<UseWorkflowPayload>(use.Proposal.Payload, McpJson.Options)!;
        Assert.Equal("M31 run", up.Name);

        var badUpdate = await new UpdateWorkflowTool().InvokeAsync(Args("""{"id":"builtin:demo","text":"# x\n- [ ] **A**"}"""), ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(badUpdate).Reason); // templates read-only

        var badDelete = await new DeleteWorkflowTool().InvokeAsync(Args("""{"id":"builtin:demo"}"""), ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(badDelete).Reason);
    }

    [Fact]
    public void VerbClasses_WritesAutoApply_DeleteAlwaysQueues()
    {
        Assert.Equal(McpVerbClass.SemanticWrite, new SaveWorkflowTool().VerbClass);
        Assert.Equal(McpVerbClass.SemanticWrite, new UpdateWorkflowTool().VerbClass);
        Assert.Equal(McpVerbClass.SemanticWrite, new SetWorkflowStepTool().VerbClass);
        Assert.Equal(McpVerbClass.SemanticWrite, new UseWorkflowTool().VerbClass);
        Assert.Equal(McpVerbClass.Destructive, new DeleteWorkflowTool().VerbClass);
    }

    [Fact]
    public async Task Appliers_DriveTheStore()
    {
        var store = NewStore(("demo", "# Demo\n- [ ] **A** — x\n"));

        await new UseWorkflowApplier(store).ApplyAsync(Proposal("use_workflow", new UseWorkflowPayload("builtin:demo", "run1")));
        var local = Assert.Single(store.ListLocal());
        Assert.Equal("local:run1", local.Id);

        await new SetWorkflowStepApplier(store).ApplyAsync(Proposal("set_workflow_step", new SetWorkflowStepPayload(local.Id, 0, true)));
        Assert.True(store.Get(local.Id)!.Doc.Steps[0].Done);

        await new UpdateWorkflowApplier(store).ApplyAsync(Proposal("update_workflow", new UpdateWorkflowPayload(local.Id, "# Renamed\n- [ ] **B** — y\n")));
        Assert.Equal("Renamed", store.Get(local.Id)!.Doc.Title);

        await new SaveWorkflowApplier(store, (_, _, _) => Task.CompletedTask)
            .ApplyAsync(Proposal("save_workflow", new SaveWorkflowPayload("agent made", "# Agent made\n- [ ] **A** — x\n", "local")));
        Assert.Equal(2, store.ListLocal().Count);

        // vospace location routes to the publisher, not the local store
        var published = new List<string>();
        await new SaveWorkflowApplier(store, (name, _, _) => { published.Add(name); return Task.CompletedTask; })
            .ApplyAsync(Proposal("save_workflow", new SaveWorkflowPayload("shared", "# S\n- [ ] **A**\n", "vospace")));
        Assert.Equal(new[] { "shared.workflow.md" }, published);
        Assert.Equal(2, store.ListLocal().Count);

        await new DeleteWorkflowApplier(store).ApplyAsync(Proposal("delete_workflow", new DeleteWorkflowPayload(local.Id)));
        Assert.Null(store.Get(local.Id));
    }

    // ── plumbing ──────────────────────────────────────────────────────────────

    private static (McpToolContext ctx, InMemoryProposalStore store) WriteCtx()
    {
        var store = new InMemoryProposalStore();
        return (McpToolContext.ForExternal("c1", Guid.NewGuid(), store, new ProposalBudget()), store);
    }

    private static PendingProposal Proposal<T>(string kind, T payload)
        => PendingProposal.Create("t", kind, "s",
            JsonSerializer.SerializeToUtf8Bytes(payload, McpJson.Options), OperationOrigin.External("c1"));
}
