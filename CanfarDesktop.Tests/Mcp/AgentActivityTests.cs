using System.Text;
using Xunit;
using CanfarDesktop.Mcp.Agents;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Proposals;

namespace CanfarDesktop.Tests.Mcp;

public class AgentActivityTests
{
    private static readonly DateTimeOffset T = new(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);

    private static PendingProposal Proposal(string kind = "save_query")
        => PendingProposal.Create("tool", kind, $"do {kind}", Encoding.UTF8.GetBytes("{}"), OperationOrigin.External("claude-desktop"));

    [Fact]
    public void Applied_CapturesProposalAndAutoFlag()
    {
        var p = Proposal();
        var entry = AgentActivityEntry.Applied(p, autoApplied: true, T);

        Assert.Equal(AgentActivityOutcome.Applied, entry.Outcome);
        Assert.True(entry.AutoApplied);
        Assert.Equal(p.Id, entry.ProposalId);
        Assert.Equal("save_query", entry.Kind);
        Assert.Equal("claude-desktop", entry.OriginLabel);
    }

    [Fact]
    public void Fingerprint_IsStableSixHexChars_AndNotTheRawId()
    {
        var fp = AgentActivityEntry.Fingerprint(OperationOrigin.External("claude-desktop"));
        Assert.Equal(6, fp.Length);
        Assert.Matches("^[0-9a-f]{6}$", fp);
        Assert.Equal(fp, AgentActivityEntry.Fingerprint(OperationOrigin.External("claude-desktop"))); // stable
        Assert.NotEqual(fp, AgentActivityEntry.Fingerprint(OperationOrigin.External("other-client")));
    }

    [Fact]
    public void Live_HasNoProposalId()
    {
        var entry = AgentActivityEntry.Live("navigate_to", "go to Search", OperationOrigin.External("c"), T);
        Assert.Equal(AgentActivityOutcome.Live, entry.Outcome);
        Assert.Null(entry.ProposalId);
    }

    [Fact]
    public void Log_IsNewestFirst_AndCapped()
    {
        var log = new AgentActivityLog();
        for (var i = 0; i < log.Cap + 10; i++)
            log.Append(AgentActivityEntry.Applied(Proposal($"k{i}"), true, T));

        Assert.Equal(log.Cap, log.Count);                       // capped
        Assert.Equal($"k{log.Cap + 9}", log.Recent(1)[0].Kind); // newest first
    }
}
