using System.Text.Json;
using Xunit;
using CanfarDesktop.Models;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Proposals;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

public class ObservationNoteWriteToolsTests
{
    private static JsonValue Args(string json) => JsonValue.Parse(json);

    private static (McpToolContext ctx, InMemoryProposalStore store) Context()
    {
        var store = new InMemoryProposalStore();
        return (McpToolContext.ForExternal("c1", Guid.NewGuid(), store, new ProposalBudget()), store);
    }

    // ── update_observation_note ───────────────────────────────────────────────

    [Fact]
    public async Task UpdateNote_BuildsProposal_WithPreview()
    {
        var (ctx, _) = Context();
        var result = await new UpdateObservationNoteTool().InvokeAsync(
            Args("""{"publisherId":"ivo://cadc/X","text":"great seeing","rating":4,"tags":["psf"]}"""), ctx, default);

        var proposal = Assert.IsType<ProposedResult>(result).Proposal;
        Assert.Equal("update_observation_note", proposal.Kind);
        Assert.Contains("great seeing", proposal.Summary);

        var payload = JsonSerializer.Deserialize<UpdateObservationNotePayload>(proposal.Payload, McpJson.Options)!;
        Assert.Equal("ivo://cadc/X", payload.PublisherId);
        Assert.Equal(4, payload.Rating);
        Assert.Equal(new[] { "psf" }, payload.Tags);
    }

    [Theory]
    [InlineData("""{"text":"x"}""")]                                  // no publisherId
    [InlineData("""{"publisherId":"X","rating":9}""")]               // rating out of range
    public async Task UpdateNote_Invalid_InvalidArgument(string json)
    {
        var (ctx, store) = Context();
        var result = await new UpdateObservationNoteTool().InvokeAsync(Args(json), ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
        Assert.Empty(store.List());
    }

    // ── bulk_update_observation_notes ─────────────────────────────────────────

    [Fact]
    public async Task BulkUpdate_BuildsProposal()
    {
        var (ctx, _) = Context();
        var result = await new BulkUpdateObservationNotesTool().InvokeAsync(
            Args("""{"items":[{"publisherId":"A","rating":5},{"publisherId":"B","text":"note"}]}"""), ctx, default);

        var proposal = Assert.IsType<ProposedResult>(result).Proposal;
        Assert.Equal("bulk_update_observation_notes", proposal.Kind);
        Assert.Contains("2 observation", proposal.Summary);
        var payload = JsonSerializer.Deserialize<BulkUpdateObservationNotesPayload>(proposal.Payload, McpJson.Options)!;
        Assert.Equal(2, payload.Items.Count);
    }

    [Theory]
    [InlineData("""{"items":[]}""")]                                  // empty
    [InlineData("""{"items":[{"publisherId":"A","rating":7}]}""")]    // bad rating in an item
    public async Task BulkUpdate_Invalid_InvalidArgument(string json)
    {
        var (ctx, store) = Context();
        var result = await new BulkUpdateObservationNotesTool().InvokeAsync(Args(json), ctx, default);
        Assert.IsType<InvalidArgument>(Assert.IsType<FailedResult>(result).Reason);
        Assert.Empty(store.List());
    }

    // ── merge ─────────────────────────────────────────────────────────────────

    private static readonly DateTimeOffset Now = new(2026, 6, 24, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Merge_ProvidedFieldsWin_OmittedKeepExisting()
    {
        var existing = new ObservationNote
        {
            PublisherID = "X", Note = "old", Rating = 2, Tags = new[] { "a" }, UpdatedUtc = Now.AddDays(-1),
        };
        var payload = new UpdateObservationNotePayload("X", Text: "new", Rating: null, Tags: null);

        var merged = ObservationNoteMerge.Apply(existing, payload, Now);

        Assert.Equal("new", merged.Note);       // provided
        Assert.Equal(2, merged.Rating);         // kept
        Assert.Equal(new[] { "a" }, merged.Tags); // kept
        Assert.Equal(Now, merged.UpdatedUtc);
    }

    [Fact]
    public void Merge_NoExisting_CreatesFromPayload()
    {
        var merged = ObservationNoteMerge.Apply(null, new UpdateObservationNotePayload("Y", "hello", 3, null), Now);
        Assert.Equal("Y", merged.PublisherID);
        Assert.Equal("hello", merged.Note);
        Assert.Equal(3, merged.Rating);
        Assert.Empty(merged.Tags);
    }

    // ── appliers ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateNoteApplier_DecodesAndInvokes()
    {
        UpdateObservationNotePayload? applied = null;
        var applier = new UpdateObservationNoteApplier(p => { applied = p; return Task.CompletedTask; });
        var proposal = PendingProposal.Create("t", "update_observation_note", "s",
            JsonSerializer.SerializeToUtf8Bytes(new UpdateObservationNotePayload("Z", "n", 1, null), McpJson.Options),
            OperationOrigin.External("c1"));

        await applier.ApplyAsync(proposal);
        Assert.Equal("Z", applied!.PublisherId);
    }

    [Fact]
    public async Task BulkApplier_DecodesAllItems()
    {
        IReadOnlyList<UpdateObservationNotePayload>? applied = null;
        var applier = new BulkUpdateObservationNotesApplier(items => { applied = items; return Task.CompletedTask; });
        var payload = new BulkUpdateObservationNotesPayload(new[]
        {
            new UpdateObservationNotePayload("A", null, 1, null),
            new UpdateObservationNotePayload("B", null, 2, null),
        });
        var proposal = PendingProposal.Create("t", "bulk_update_observation_notes", "s",
            JsonSerializer.SerializeToUtf8Bytes(payload, McpJson.Options), OperationOrigin.External("c1"));

        await applier.ApplyAsync(proposal);
        Assert.Equal(2, applied!.Count);
    }
}
