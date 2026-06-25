using System.Text;
using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Proposals;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

public class VoSpaceWriteToolsTests
{
    private static JsonValue Args(string json) => JsonValue.Parse(json);

    private static (McpToolContext ctx, InMemoryProposalStore store) Context()
    {
        var store = new InMemoryProposalStore();
        return (McpToolContext.ForExternal("c1", Guid.NewGuid(), store, new ProposalBudget()), store);
    }

    [Fact]
    public async Task UploadText_BuildsProposal()
    {
        var (ctx, _) = Context();
        var result = await new UploadTextToVoSpaceTool().InvokeAsync(
            Args("""{"path":"/home/u/run.py","content":"print(1)"}"""), ctx, default);
        var payload = JsonSerializer.Deserialize<UploadTextPayload>(Assert.IsType<ProposedResult>(result).Proposal.Payload, McpJson.Options)!;
        Assert.Equal("/home/u/run.py", payload.Path);
        Assert.Equal("print(1)", payload.Content);
    }

    [Fact]
    public async Task UploadText_OverSizeLimit_InvalidArgument()
    {
        var (ctx, store) = Context();
        var big = new string('x', UploadTextToVoSpaceTool.MaxContentBytes + 1);
        var result = await new UploadTextToVoSpaceTool().InvokeAsync(
            Args(JsonSerializer.Serialize(new { path = "/p", content = big })), ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
        Assert.Empty(store.List());
    }

    [Fact]
    public async Task CreateFolder_BuildsProposal()
    {
        var (ctx, _) = Context();
        var result = await new CreateVoSpaceFolderTool().InvokeAsync(Args("""{"path":"/home/u","name":"data"}"""), ctx, default);
        var payload = JsonSerializer.Deserialize<CreateFolderPayload>(Assert.IsType<ProposedResult>(result).Proposal.Payload, McpJson.Options)!;
        Assert.Equal("data", payload.Name);
    }

    [Fact]
    public void VerbClasses()
    {
        Assert.Equal(McpVerbClass.SemanticWrite, new UploadTextToVoSpaceTool().VerbClass);
        Assert.Equal(McpVerbClass.SemanticWrite, new CreateVoSpaceFolderTool().VerbClass);
        Assert.Equal(McpVerbClass.Destructive, new DeleteVoSpaceNodeTool().VerbClass);
    }

    [Fact]
    public async Task DeleteNode_MissingPath_InvalidArgument()
    {
        var (ctx, _) = Context();
        var result = await new DeleteVoSpaceNodeTool().InvokeAsync(Args("""{"path":"  "}"""), ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
    }

    [Fact]
    public async Task Appliers_DecodeAndInvoke()
    {
        string? uploaded = null, created = null, deleted = null;
        var up = new UploadTextToVoSpaceApplier(p => { uploaded = p.Path; return Task.CompletedTask; });
        var mk = new CreateVoSpaceFolderApplier(p => { created = p.Name; return Task.CompletedTask; });
        var rm = new DeleteVoSpaceNodeApplier(p => { deleted = p.Path; return Task.CompletedTask; });

        await up.ApplyAsync(Proposal("upload_text_to_vospace", new UploadTextPayload("/p", "c", null)));
        await mk.ApplyAsync(Proposal("create_vospace_folder", new CreateFolderPayload("/parent", "f")));
        await rm.ApplyAsync(Proposal("delete_vospace_node", new DeleteNodePayload("/gone")));

        Assert.Equal("/p", uploaded);
        Assert.Equal("f", created);
        Assert.Equal("/gone", deleted);
    }

    private static PendingProposal Proposal<T>(string kind, T payload)
        => PendingProposal.Create("t", kind, "s", JsonSerializer.SerializeToUtf8Bytes(payload, McpJson.Options), OperationOrigin.External("c1"));
}
