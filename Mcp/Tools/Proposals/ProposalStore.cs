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

    /// <summary>Raised after any mutation (enqueue or resolve) so UI badges can track the pending count.
    /// Raised outside the lock; may fire on any thread.</summary>
    public event Action? Changed;

    /// <summary>
    /// Richer lifecycle event for the agent event log (<c>list_events</c>): which proposal arrived or
    /// resolved and how. Raised outside the lock; may fire on any thread.
    /// </summary>
    public event Action<ProposalStoreEvent>? EventOccurred;

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
        }
        Changed?.Invoke();
        EventOccurred?.Invoke(new ProposalStoreEvent("proposalArrived", proposal));
        return proposal;
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

    /// <summary>Pending count without snapshotting the list (UI badges poll this per mutation).</summary>
    public int PendingCount
    {
        get { lock (_gate) return _pending.Count; }
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
        PendingProposal proposal;
        lock (_gate)
        {
            if (!_pending.TryGetValue(id, out proposal!)) return false;
            _pending.Remove(id);
            _pendingOrder.Remove(id);
            _tombstones.Add((id, state, _clock()));
            if (_tombstones.Count > TombstoneCap)
                _tombstones.RemoveRange(0, _tombstones.Count - TombstoneCap);
        }
        Changed?.Invoke();
        EventOccurred?.Invoke(new ProposalStoreEvent(state switch
        {
            ProposalState.Applied => "proposalApplied",
            ProposalState.Rejected => "proposalRejected",
            _ => "proposalWithdrawn",
        }, proposal));
        return true;
    }

    private void GcTombstones()
    {
        var cutoff = _clock() - TombstoneTtl;
        _tombstones.RemoveAll(t => t.At < cutoff);
    }
}

/// <summary>One proposal-lifecycle occurrence for the agent event log.</summary>
public readonly record struct ProposalStoreEvent(string Kind, PendingProposal Proposal);

/// <summary>
/// Token-cursor ring buffer of proposal-lifecycle events backing the <c>list_events</c> tool: an agent
/// polls with its last token and receives only newer entries plus the next token. Thread-safe.
/// 1-to-1 with the macOS AgentEventLog.
/// </summary>
public sealed class AgentEventLog
{
    public int Cap { get; } = 512;

    private readonly object _gate = new();
    private readonly LinkedList<AgentEvent> _entries = new();
    private ulong _nextToken = 1;

    public ulong Append(string kind, PendingProposal proposal, DateTimeOffset now)
    {
        lock (_gate)
        {
            var entry = new AgentEvent(_nextToken++, now, kind, proposal.Id, proposal.Kind,
                kind == "proposalArrived" ? (proposal.Origin.IsExternal ? "external" : "user") : null);
            _entries.AddLast(entry);
            while (_entries.Count > Cap) _entries.RemoveFirst();
            return entry.Token;
        }
    }

    public ulong CurrentToken
    {
        get { lock (_gate) return _nextToken - 1; }
    }

    /// <summary>
    /// Entries newer than <paramref name="since"/>, plus the cursor for the next poll — captured
    /// under ONE lock so an append racing the read can't produce a NextToken that skips an event
    /// the caller never received. <c>Expired</c> is true when the token predates the retained
    /// buffer (events were lost) — the caller should re-baseline with an empty token.
    /// </summary>
    public (IReadOnlyList<AgentEvent> Entries, bool Expired, ulong NextToken) EntriesSince(ulong since)
    {
        lock (_gate)
        {
            var oldest = _entries.First?.Value.Token;
            var expired = since > 0 && (oldest is null ? since < _nextToken - 1 : since < oldest.Value - 1);
            return (_entries.Where(e => e.Token > since).ToList(), expired, _nextToken - 1);
        }
    }
}

/// <summary>One retained agent event (see <see cref="AgentEventLog"/>).</summary>
public sealed record AgentEvent(
    ulong Token, DateTimeOffset OccurredAt, string Kind, Guid ProposalId, string ProposalKind, string? OriginKind);
