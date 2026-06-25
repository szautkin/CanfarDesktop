using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Proposals;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

public class ObservationWriteToolsTests
{
    private static JsonValue Args(string json) => JsonValue.Parse(json);

    private static (McpToolContext ctx, InMemoryProposalStore store) Context()
    {
        var store = new InMemoryProposalStore();
        return (McpToolContext.ForExternal("c1", Guid.NewGuid(), store, new ProposalBudget()), store);
    }

    [Fact]
    public async Task DownloadObservation_BuildsProposal()
    {
        var (ctx, _) = Context();
        var result = await new DownloadObservationTool().InvokeAsync(Args("""{"publisherId":"ivo://cadc/X"}"""), ctx, default);
        var proposal = Assert.IsType<ProposedResult>(result).Proposal;
        Assert.Equal("download_observation", proposal.Kind);
        var payload = JsonSerializer.Deserialize<DownloadObservationPayload>(proposal.Payload, McpJson.Options)!;
        Assert.Equal("ivo://cadc/X", payload.PublisherId);
    }

    [Fact]
    public async Task DownloadObservation_MissingId_InvalidArgument()
    {
        var (ctx, store) = Context();
        var result = await new DownloadObservationTool().InvokeAsync(Args("""{"publisherId":"  "}"""), ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
        Assert.Empty(store.List());
    }

    [Fact]
    public void VerbClasses()
    {
        Assert.Equal(McpVerbClass.SemanticWrite, new DownloadObservationTool().VerbClass);
        Assert.Equal(McpVerbClass.Destructive, new DeleteDownloadedObservationTool().VerbClass);
    }

    [Fact]
    public async Task Appliers_DecodeAndInvoke()
    {
        string? downloaded = null, deleted = null;
        var dl = new DownloadObservationApplier(p => { downloaded = p.PublisherId; return Task.CompletedTask; });
        var del = new DeleteDownloadedObservationApplier(p => { deleted = p.Id; return Task.CompletedTask; });

        await dl.ApplyAsync(Proposal("download_observation", new DownloadObservationPayload("ivo://X")));
        await del.ApplyAsync(Proposal("delete_downloaded_observation", new DeleteDownloadedObservationPayload("obs-1")));

        Assert.Equal("ivo://X", downloaded);
        Assert.Equal("obs-1", deleted);
    }

    private static PendingProposal Proposal<T>(string kind, T payload)
        => PendingProposal.Create("t", kind, "s", JsonSerializer.SerializeToUtf8Bytes(payload, McpJson.Options), OperationOrigin.External("c1"));
}
