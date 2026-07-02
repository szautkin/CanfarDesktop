using CanfarDesktop.Models;
using CanfarDesktop.Mcp.Tools.Proposals;

namespace CanfarDesktop.Mcp.Agents;

/// <summary>Builds the <see cref="AgentAttribution"/> stamp appliers leave on agent-authored entities.</summary>
public static class AgentAttributionStamp
{
    /// <summary>
    /// The stamp for an agent-originated proposal at apply time; null for user-originated operations
    /// (no badge for the in-app human).
    /// </summary>
    public static AgentAttribution? ForProposal(PendingProposal proposal)
        => proposal.Origin.IsExternal
            ? new(proposal.Id,
                AgentActivityEntry.Fingerprint(proposal.Origin),
                proposal.Origin.Label,
                DateTimeOffset.UtcNow,
                proposal.Summary)
            : null;
}
