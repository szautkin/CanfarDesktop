using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Proposals;
using CanfarDesktop.Mcp.Tools.Read;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

public class ListEventsToolTests
{
    private static JsonValue Args(string json) => JsonValue.Parse(json);

    private static McpToolContext Ctx() => McpToolContext.ForExternal("c1", Guid.NewGuid());

    private static PendingProposal Proposal(string kind, OperationOrigin? origin = null)
        => PendingProposal.Create("t", kind, "s", "{}"u8.ToArray(), origin ?? OperationOrigin.External("c1"));

    private static async Task<JsonElement> InvokeAsync(AgentEventLog log, string argsJson)
    {
        var result = await new ListEventsTool(log).InvokeAsync(Args(argsJson), Ctx(), default);
        return JsonDocument.Parse(Assert.IsType<DataResult>(result).Json).RootElement;
    }

    [Fact]
    public void VerbClass_IsProposalLifecycle_AndAgentSafe()
    {
        var tool = new ListEventsTool(new AgentEventLog());
        Assert.Equal(McpVerbClass.ProposalLifecycle, tool.VerbClass);
        Assert.True(tool.AgentSafe);
        Assert.Equal("list_events", tool.Descriptor.Name);
    }

    [Fact]
    public async Task EmptyLog_ReturnsNoEvents_TokenZero_NotExpired()
    {
        var doc = await InvokeAsync(new AgentEventLog(), "{}");
        Assert.Equal(0, doc.GetProperty("events").GetArrayLength());
        Assert.Equal("0", doc.GetProperty("nextToken").GetString());
        Assert.False(doc.GetProperty("expired").GetBoolean());
    }

    [Fact]
    public async Task AbsentToken_ReadsFromBufferStart_WithFullItemShape()
    {
        var log = new AgentEventLog();
        var proposal = Proposal("save_query");
        var at = new DateTimeOffset(2026, 7, 2, 12, 30, 45, TimeSpan.Zero);
        log.Append("proposalArrived", proposal, at);

        var doc = await InvokeAsync(log, "{}");
        var events = doc.GetProperty("events");
        Assert.Equal(1, events.GetArrayLength());
        var item = events[0];
        Assert.Equal("1", item.GetProperty("token").GetString());
        Assert.Equal("2026-07-02T12:30:45Z", item.GetProperty("occurredAtISO").GetString());
        Assert.Equal("proposalArrived", item.GetProperty("kind").GetString());
        Assert.Equal(proposal.Id.ToString(), item.GetProperty("proposalID").GetString());
        Assert.Equal("save_query", item.GetProperty("proposalKind").GetString());
        Assert.Equal("external", item.GetProperty("originKind").GetString());
        Assert.Equal("1", doc.GetProperty("nextToken").GetString());
    }

    [Fact]
    public async Task SinceToken_ReturnsOnlyNewerEvents()
    {
        var log = new AgentEventLog();
        log.Append("proposalArrived", Proposal("k1"), DateTimeOffset.UtcNow);
        log.Append("proposalApplied", Proposal("k2"), DateTimeOffset.UtcNow);
        log.Append("proposalRejected", Proposal("k3"), DateTimeOffset.UtcNow);

        var doc = await InvokeAsync(log, """{"since_token":"1"}""");
        var events = doc.GetProperty("events");
        Assert.Equal(2, events.GetArrayLength());
        Assert.Equal("proposalApplied", events[0].GetProperty("kind").GetString());
        Assert.Equal("proposalRejected", events[1].GetProperty("kind").GetString());
        Assert.Equal("3", doc.GetProperty("nextToken").GetString());
        Assert.False(doc.GetProperty("expired").GetBoolean());
    }

    [Fact]
    public async Task ResolutionEvents_OmitOriginKind()
    {
        var log = new AgentEventLog();
        log.Append("proposalWithdrawn", Proposal("k"), DateTimeOffset.UtcNow);

        var doc = await InvokeAsync(log, "{}");
        // originKind only accompanies proposalArrived; McpJson omits nulls entirely.
        Assert.False(doc.GetProperty("events")[0].TryGetProperty("originKind", out _));
    }

    [Fact]
    public async Task UserOriginArrival_ReportsUserOriginKind()
    {
        var log = new AgentEventLog();
        log.Append("proposalArrived", Proposal("k", OperationOrigin.User), DateTimeOffset.UtcNow);

        var doc = await InvokeAsync(log, "{}");
        Assert.Equal("user", doc.GetProperty("events")[0].GetProperty("originKind").GetString());
    }

    [Theory]
    [InlineData("""{"since_token":""}""")]
    [InlineData("""{"since_token":"not-a-number"}""")]
    public async Task EmptyOrMalformedToken_ReadsFromBufferStart(string argsJson)
    {
        var log = new AgentEventLog();
        log.Append("proposalArrived", Proposal("k"), DateTimeOffset.UtcNow);

        var doc = await InvokeAsync(log, argsJson);
        Assert.Equal(1, doc.GetProperty("events").GetArrayLength());
        Assert.False(doc.GetProperty("expired").GetBoolean());
    }

    [Fact]
    public async Task TokenOlderThanRetainedBuffer_SetsExpired()
    {
        var log = new AgentEventLog();
        for (var i = 0; i < log.Cap + 5; i++)
            log.Append("proposalArrived", Proposal("k"), DateTimeOffset.UtcNow);

        // Token 1 was trimmed out of the ring buffer — the agent must re-baseline.
        var doc = await InvokeAsync(log, """{"since_token":"1"}""");
        Assert.True(doc.GetProperty("expired").GetBoolean());
    }

    [Fact]
    public async Task StoreWiring_ArrivalAndResolution_FlowIntoTheLog()
    {
        // The same wiring McpHost.Start uses: store events append into the log.
        var log = new AgentEventLog();
        var store = new InMemoryProposalStore();
        store.EventOccurred += e => log.Append(e.Kind, e.Proposal, DateTimeOffset.UtcNow);

        var proposal = store.Enqueue(Proposal("delete_session"));
        store.MarkApplied(proposal.Id);

        var doc = await InvokeAsync(log, "{}");
        var events = doc.GetProperty("events");
        Assert.Equal(2, events.GetArrayLength());
        Assert.Equal("proposalArrived", events[0].GetProperty("kind").GetString());
        Assert.Equal("proposalApplied", events[1].GetProperty("kind").GetString());
        Assert.Equal(proposal.Id.ToString(), events[1].GetProperty("proposalID").GetString());
    }
}
