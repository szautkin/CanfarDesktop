using System.Security.Cryptography;
using System.Text;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Proposals;

namespace CanfarDesktop.Mcp.Agents;

/// <summary>What happened to an agent-originated change. 1-to-1 with the macOS activity outcome.</summary>
public enum AgentActivityOutcome { Applied, Rejected, Withdrawn, Live }

/// <summary>
/// One breadcrumb in the agent-activity feed: a write that was applied/rejected/withdrawn, or a live
/// view-state op. Lets the user review what an agent did — especially under auto-apply, where writes
/// land without a click. The origin is shown as a short fingerprint + label (never the raw client id).
/// 1-to-1 with the macOS AgentActivityEntry.
/// </summary>
public sealed record AgentActivityEntry(
    Guid Id,
    DateTimeOffset Timestamp,
    string Kind,
    string Summary,
    string OriginFingerprint,
    string OriginLabel,
    Guid? ProposalId,
    AgentActivityOutcome Outcome,
    bool AutoApplied)
{
    public static AgentActivityEntry Applied(PendingProposal proposal, bool autoApplied, DateTimeOffset now)
        => new(Guid.NewGuid(), now, proposal.Kind, proposal.Summary,
            Fingerprint(proposal.Origin), proposal.Origin.Label, proposal.Id, AgentActivityOutcome.Applied, autoApplied);

    public static AgentActivityEntry Rejected(PendingProposal proposal, DateTimeOffset now)
        => new(Guid.NewGuid(), now, proposal.Kind, proposal.Summary,
            Fingerprint(proposal.Origin), proposal.Origin.Label, proposal.Id, AgentActivityOutcome.Rejected, false);

    public static AgentActivityEntry Withdrawn(PendingProposal proposal, DateTimeOffset now)
        => new(Guid.NewGuid(), now, proposal.Kind, proposal.Summary,
            Fingerprint(proposal.Origin), proposal.Origin.Label, proposal.Id, AgentActivityOutcome.Withdrawn, false);

    public static AgentActivityEntry Live(string kind, string summary, OperationOrigin origin, DateTimeOffset now)
        => new(Guid.NewGuid(), now, kind, summary, Fingerprint(origin), origin.Label, null, AgentActivityOutcome.Live, false);

    /// <summary>A short, stable, non-reversible tag for the origin (6 hex chars of SHA-256 over its label).</summary>
    public static string Fingerprint(OperationOrigin origin)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(origin.Label))).ToLowerInvariant()[..6];
}

/// <summary>
/// Newest-first, capped, thread-safe ring of agent-activity entries. The host appends on apply / live
/// op; the UI reads <see cref="Recent"/> to show the feed.
/// </summary>
public sealed class AgentActivityLog
{
    public int Cap { get; } = 200;

    private readonly object _gate = new();
    private readonly LinkedList<AgentActivityEntry> _entries = new();

    public void Append(AgentActivityEntry entry)
    {
        lock (_gate)
        {
            _entries.AddFirst(entry);
            while (_entries.Count > Cap) _entries.RemoveLast();
        }
    }

    public IReadOnlyList<AgentActivityEntry> Recent(int max = 50)
    {
        lock (_gate) return _entries.Take(max).ToList();
    }

    public int Count
    {
        get { lock (_gate) return _entries.Count; }
    }
}
