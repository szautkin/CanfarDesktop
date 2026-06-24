namespace CanfarDesktop.Mcp.Tools.Proposals;

/// <summary>
/// Per-origin cap on how many proposals one caller may queue before the user clears the strip — defends
/// against a runaway agent loop. Consulted by the router AFTER a write tool enqueues its proposal; on
/// refusal the router withdraws that proposal so no partial batch lands. Reset on a turn/session
/// boundary (chat: end of turn; external: MCP disconnect; the user is never capped). Thread-safe.
/// 1-to-1 with the macOS ProposalBudget actor.
/// </summary>
public sealed class ProposalBudget
{
    public int Limit { get; }

    private readonly object _gate = new();
    private readonly Dictionary<OperationOrigin, int> _counts = new();

    public ProposalBudget(int limit = 8)
    {
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit), "ProposalBudget limit must be positive.");
        Limit = limit;
    }

    /// <summary>Reserve a slot for an origin; false when its per-turn cap would be exceeded.</summary>
    public bool TryAccept(OperationOrigin origin)
    {
        lock (_gate)
        {
            var current = _counts.GetValueOrDefault(origin);
            if (current >= Limit) return false;
            _counts[origin] = current + 1;
            return true;
        }
    }

    /// <summary>Remaining proposals for an origin (surfaced to agents so they can self-throttle).</summary>
    public int Remaining(OperationOrigin origin)
    {
        lock (_gate) return Math.Max(0, Limit - _counts.GetValueOrDefault(origin));
    }

    /// <summary>Reset one origin's count on a turn/session boundary.</summary>
    public void Reset(OperationOrigin origin)
    {
        lock (_gate) _counts.Remove(origin);
    }

    /// <summary>Reset all counts (app shutdown / tests).</summary>
    public void ResetAll()
    {
        lock (_gate) _counts.Clear();
    }
}
