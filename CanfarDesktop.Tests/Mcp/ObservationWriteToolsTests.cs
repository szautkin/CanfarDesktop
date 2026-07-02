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
        Assert.Null(payload.ArtifactIndex); // default = primary artifact
    }

    [Fact]
    public async Task DownloadObservation_WithArtifactIndex_PayloadCarriesIt()
    {
        var (ctx, _) = Context();
        var result = await new DownloadObservationTool().InvokeAsync(
            Args("""{"publisherId":"ivo://cadc/X","artifactIndex":3}"""), ctx, default);
        var proposal = Assert.IsType<ProposedResult>(result).Proposal;
        var payload = JsonSerializer.Deserialize<DownloadObservationPayload>(proposal.Payload, McpJson.Options)!;
        Assert.Equal(3, payload.ArtifactIndex); // SCI-5: pick a specific DataLink artifact
        Assert.Contains("artifact #3", proposal.Summary);
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

    // ── download_observations_bulk ────────────────────────────────────────────

    [Fact]
    public async Task DownloadBulk_BuildsProposal_WithPerItemPayloads()
    {
        var (ctx, _) = Context();
        var result = await new DownloadObservationsBulkTool().InvokeAsync(
            Args("""{"items":[{"publisherId":"ivo://cadc/A"},{"publisherId":" ivo://cadc/B ","artifactIndex":2}]}"""), ctx, default);

        var proposal = Assert.IsType<ProposedResult>(result).Proposal;
        Assert.Equal("download_observations_bulk", proposal.Kind);
        Assert.Equal("Download 2 observations", proposal.Summary);
        var payload = JsonSerializer.Deserialize<DownloadObservationsBulkPayload>(proposal.Payload, McpJson.Options)!;
        Assert.Equal(2, payload.Items.Count);
        Assert.Equal("ivo://cadc/A", payload.Items[0].PublisherId);
        Assert.Null(payload.Items[0].ArtifactIndex);
        Assert.Equal("ivo://cadc/B", payload.Items[1].PublisherId); // trimmed
        Assert.Equal(2, payload.Items[1].ArtifactIndex);
    }

    [Fact]
    public async Task DownloadBulk_SingleItem_SingularSummary()
    {
        var (ctx, _) = Context();
        var result = await new DownloadObservationsBulkTool().InvokeAsync(
            Args("""{"items":[{"publisherId":"ivo://cadc/A"}]}"""), ctx, default);
        Assert.Equal("Download 1 observation", Assert.IsType<ProposedResult>(result).Proposal.Summary);
    }

    [Theory]
    [InlineData("""{"items":[]}""")]                          // empty
    [InlineData("""{}""")]                                    // absent
    [InlineData("""{"items":[{"publisherId":"  "}]}""")]      // blank id
    public async Task DownloadBulk_InvalidItems_Rejected(string argsJson)
    {
        var (ctx, store) = Context();
        var result = await new DownloadObservationsBulkTool().InvokeAsync(Args(argsJson), ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
        Assert.Empty(store.List());
    }

    [Fact]
    public async Task DownloadBulk_OverFiftyItems_Rejected()
    {
        var (ctx, store) = Context();
        var items = string.Join(",", Enumerable.Range(0, 51).Select(i => $$"""{"publisherId":"ivo://cadc/{{i}}"}"""));
        var result = await new DownloadObservationsBulkTool().InvokeAsync(Args($$"""{"items":[{{items}}]}"""), ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
        Assert.Empty(store.List());
    }

    [Fact]
    public async Task DownloadBulkApplier_RunsEachItemInSequence_WithTheProposalAttribution()
    {
        var seen = new List<(string Pid, int? Artifact, string? Client)>();
        var ap = new DownloadObservationsBulkApplier((p, attribution) =>
        {
            seen.Add((p.PublisherId, p.ArtifactIndex, attribution?.OriginLabel));
            return Task.CompletedTask;
        });

        await ap.ApplyAsync(Proposal("download_observations_bulk", new DownloadObservationsBulkPayload(new[]
        {
            new DownloadObservationPayload("ivo://A"),
            new DownloadObservationPayload("ivo://B", 3),
        })));

        Assert.Equal(2, seen.Count);
        Assert.Equal(("ivo://A", (int?)null, "c1"), seen[0]); // external origin → attribution stamped
        Assert.Equal(("ivo://B", (int?)3, "c1"), seen[1]);
    }

    [Fact]
    public async Task DownloadBulkApplier_FirstFailure_AbortsTheRest()
    {
        var attempted = new List<string>();
        var ap = new DownloadObservationsBulkApplier((p, _) =>
        {
            attempted.Add(p.PublisherId);
            return p.PublisherId == "ivo://B" ? throw new InvalidOperationException("boom") : Task.CompletedTask;
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ap.ApplyAsync(Proposal("download_observations_bulk", new DownloadObservationsBulkPayload(new[]
            {
                new DownloadObservationPayload("ivo://A"),
                new DownloadObservationPayload("ivo://B"),
                new DownloadObservationPayload("ivo://C"),
            }))));

        Assert.Equal(new[] { "ivo://A", "ivo://B" }, attempted); // C never starts
    }

    // ── clear_research_archive ────────────────────────────────────────────────

    [Fact]
    public async Task ClearArchive_BuildsDestructiveProposal_WithVerbatimSummary()
    {
        var (ctx, _) = Context();
        var tool = new ClearResearchArchiveTool();
        Assert.Equal(McpVerbClass.Destructive, tool.VerbClass);

        var result = await tool.InvokeAsync(Args("{}"), ctx, default);
        var proposal = Assert.IsType<ProposedResult>(result).Proposal;
        Assert.Equal("clear_research_archive", proposal.Kind);
        Assert.Equal("Clear ALL research archive records", proposal.Summary);
    }

    [Fact]
    public async Task ClearArchiveApplier_RemovesEveryObservation_ItsFile_AndItsNotes()
    {
        var observations = new List<CanfarDesktop.Models.DownloadedObservation>
        {
            new() { PublisherID = "ivo://A", LocalPath = @"C:\d\a.fits" },
            new() { PublisherID = "ivo://B", LocalPath = "" }, // no file → no delete attempt
        };
        var removed = new List<string>();
        var notesDeleted = new List<string>();
        var filesDeleted = new List<string>();

        var ap = new ClearResearchArchiveApplier(
            () => observations.ToList(),
            o => { removed.Add(o.PublisherID); observations.Remove(o); },
            notesDeleted.Add,
            filesDeleted.Add);

        await ap.ApplyAsync(Proposal("clear_research_archive", new ClearResearchArchivePayload()));

        Assert.Equal(new[] { "ivo://A", "ivo://B" }, removed);
        Assert.Equal(new[] { "ivo://A", "ivo://B" }, notesDeleted);
        Assert.Equal(new[] { @"C:\d\a.fits" }, filesDeleted);
        Assert.Empty(observations);
    }

    [Fact]
    public async Task ClearArchiveApplier_FileDeleteFailure_IsBestEffort()
    {
        var removed = new List<string>();
        var ap = new ClearResearchArchiveApplier(
            () => new[]
            {
                new CanfarDesktop.Models.DownloadedObservation { PublisherID = "ivo://A", LocalPath = @"C:\locked.fits" },
                new CanfarDesktop.Models.DownloadedObservation { PublisherID = "ivo://B", LocalPath = @"C:\ok.fits" },
            },
            o => removed.Add(o.PublisherID),
            _ => { },
            _ => throw new IOException("locked")); // every file delete fails

        await ap.ApplyAsync(Proposal("clear_research_archive", new ClearResearchArchivePayload())); // must not throw
        Assert.Equal(new[] { "ivo://A", "ivo://B" }, removed); // records still cleared
    }

    private static PendingProposal Proposal<T>(string kind, T payload)
        => PendingProposal.Create("t", kind, "s", JsonSerializer.SerializeToUtf8Bytes(payload, McpJson.Options), OperationOrigin.External("c1"));
}
