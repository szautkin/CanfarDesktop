namespace CanfarDesktop.Mcp.Tools.Proposals;

/// <summary>
/// The pending-write queue. Tools enqueue proposals; the strip UI / auto-apply resolve them. After a
/// proposal resolves, a tombstone answers <see cref="State"/> for a retention window so an agent can
/// poll the outcome. 1-to-1 with the macOS ProposalStore protocol.
/// </summary>
public interface IProposalStore
{
    PendingProposal Enqueue(PendingProposal proposal);

    /// <summary>Pending proposals in FIFO order, optionally filtered to one origin.</summary>
    IReadOnlyList<PendingProposal> List(OperationOrigin? origin = null);

    /// <summary>The pending proposal with this id, or null if absent/resolved (for the apply path).</summary>
    PendingProposal? Get(Guid id);

    ProposalState State(Guid id);

    bool MarkApplied(Guid id);
    bool MarkRejected(Guid id);
    bool Withdraw(Guid id);
}

/// <summary>
/// In-memory <see cref="IProposalStore"/>. Pending proposals keep FIFO insertion order; resolved ids
/// leave a tombstone (5-min TTL, capped at 256, FIFO-trimmed) so <see cref="State"/> reports the
/// outcome for a while before returning <see cref="ProposalState.Unknown"/>. Thread-safe. 1-to-1 with
/// the macOS InMemoryProposalStore. The clock is injectable so the TTL/cap are deterministically testable.
/// </summary>
public sealed class InMemoryProposalStore : IProposalStore
{
    public TimeSpan TombstoneTtl { get; } = TimeSpan.FromMinutes(5);
    public int TombstoneCap { get; } = 256;

    private readonly object _gate = new();
    private readonly Dictionary<Guid, PendingProposal> _pending = new();
    private readonly List<Guid> _pendingOrder = new();
    private readonly List<(Guid Id, ProposalState State, DateTimeOffset At)> _tombstones = new();
    private readonly Func<DateTimeOffset> _clock;

    public InMemoryProposalStore(Func<DateTimeOffset>? clock = null)
        => _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public PendingProposal Enqueue(PendingProposal proposal)
    {
        lock (_gate)
        {
            _pending[proposal.Id] = proposal;
            _pendingOrder.Add(proposal.Id);
            return proposal;
        }
    }

    public IReadOnlyList<PendingProposal> List(OperationOrigin? origin = null)
    {
        lock (_gate)
            return _pendingOrder
                .Select(id => _pending[id])
                .Where(p => origin is null || p.Origin == origin)
                .ToList();
    }

    public PendingProposal? Get(Guid id)
    {
        lock (_gate) return _pending.TryGetValue(id, out var p) ? p : null;
    }

    public ProposalState State(Guid id)
    {
        lock (_gate)
        {
            GcTombstones();
            if (_pending.ContainsKey(id)) return ProposalState.Pending;
            foreach (var t in _tombstones)
                if (t.Id == id) return t.State;
            return ProposalState.Unknown;
        }
    }

    public bool MarkApplied(Guid id) => Resolve(id, ProposalState.Applied);
    public bool MarkRejected(Guid id) => Resolve(id, ProposalState.Rejected);
    public bool Withdraw(Guid id) => Resolve(id, ProposalState.Withdrawn);

    private bool Resolve(Guid id, ProposalState state)
    {
        lock (_gate)
        {
            if (!_pending.Remove(id)) return false;
            _pendingOrder.Remove(id);
            _tombstones.Add((id, state, _clock()));
            if (_tombstones.Count > TombstoneCap)
                _tombstones.RemoveRange(0, _tombstones.Count - TombstoneCap);
            return true;
        }
    }

    private void GcTombstones()
    {
        var cutoff = _clock() - TombstoneTtl;
        _tombstones.RemoveAll(t => t.At < cutoff);
    }
}
