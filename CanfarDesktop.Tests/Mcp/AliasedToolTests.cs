using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Proposals;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

public class AliasedToolTests
{
    private static JsonValue Args(string json) => JsonValue.Parse(json);

    private static (McpToolContext ctx, InMemoryProposalStore store) Context()
    {
        var store = new InMemoryProposalStore();
        return (McpToolContext.ForExternal("c1", Guid.NewGuid(), store, new ProposalBudget()), store);
    }

    [Fact]
    public void Descriptor_CarriesAliasNameAndDescription_ButTheInnerSchema()
    {
        var inner = new CreateVoSpaceFolderTool();
        var alias = new AliasedTool("vospace_mkdir", "Create a folder under a VOSpace path.", inner);

        Assert.Equal("vospace_mkdir", alias.Descriptor.Name);
        Assert.Equal("Create a folder under a VOSpace path.", alias.Descriptor.Description);
        Assert.Same(inner.Descriptor.InputSchema, alias.Descriptor.InputSchema);
        Assert.Equal(inner.VerbClass, alias.VerbClass);
        Assert.Equal(inner.AgentSafe, alias.AgentSafe);
    }

    [Fact]
    public async Task Invoke_DelegatesToTheInnerTool_ProducingTheSameProposalKind()
    {
        var (ctx, store) = Context();
        var alias = new AliasedTool("vospace_mkdir", "Create a folder under a VOSpace path.", new CreateVoSpaceFolderTool());

        var result = await alias.InvokeAsync(Args("""{"path":"/home/u","name":"data"}"""), ctx, default);

        var proposal = Assert.IsType<ProposedResult>(result).Proposal;
        Assert.Equal("create_vospace_folder", proposal.Kind); // same kind → existing applier applies it
        var payload = JsonSerializer.Deserialize<CreateFolderPayload>(proposal.Payload, McpJson.Options)!;
        Assert.Equal("data", payload.Name);
        Assert.Single(store.List());
    }

    [Fact]
    public async Task Invoke_InnerValidationStillApplies()
    {
        var (ctx, store) = Context();
        var alias = new AliasedTool("upload_to_vospace", "Upload a downloaded observation's local file to a VOSpace path.",
            new UploadFileToVoSpaceTool());

        var result = await alias.InvokeAsync(Args("""{"localPath":"relative.fits","vospacePath":"/x"}"""), ctx, default);

        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
        Assert.Empty(store.List());
    }

    [Fact]
    public void Router_ExposesBothTheAliasAndTheOriginal_AndDispatchesEach()
    {
        var inner = new CreateVoSpaceFolderTool();
        var alias = new AliasedTool("vospace_mkdir", "Create a folder under a VOSpace path.", inner);
        var router = new McpToolRouter(new IMcpTool[] { inner, alias });

        Assert.Contains("create_vospace_folder", router.ToolNames);
        Assert.Contains("vospace_mkdir", router.ToolNames);
        Assert.Contains(router.ExternalManifest, d => d.Name == "vospace_mkdir");
        Assert.Contains(router.ExternalManifest, d => d.Name == "create_vospace_folder");
    }

    [Fact]
    public async Task Router_DispatchesTheAliasName_ToTheInnerTool()
    {
        var (ctx, store) = Context();
        var inner = new CreateVoSpaceFolderTool();
        var alias = new AliasedTool("vospace_mkdir", "Create a folder under a VOSpace path.", inner);
        var router = new McpToolRouter(new IMcpTool[] { inner, alias });

        var result = await router.DispatchAsync("vospace_mkdir", Args("""{"path":"/home/u","name":"d"}"""), ctx, default);

        var proposal = Assert.IsType<ProposedResult>(result).Proposal;
        Assert.Equal("create_vospace_folder", proposal.Kind);
        Assert.Single(store.List());
    }
}
