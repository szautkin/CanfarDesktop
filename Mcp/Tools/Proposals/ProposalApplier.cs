namespace CanfarDesktop.Mcp.Tools.Proposals;

/// <summary>
/// Applies one kind of proposal to the real backing store. Each applier owns the exact store/service it
/// mutates and decodes the proposal payload itself. 1-to-1 with the macOS ProposalApplier protocol.
/// </summary>
public interface IProposalApplier
{
    /// <summary>The proposal <see cref="PendingProposal.Kind"/> this applier handles.</summary>
    string Kind { get; }

    Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default);
}

/// <summary>Thrown by an applier (or the registry) when a proposal can't be applied.</summary>
public sealed class ProposalApplyException : Exception
{
    public ProposalApplyException(string message) : base(message) { }

    public static ProposalApplyException NoApplierForKind(string kind) => new($"No applier registered for proposal kind '{kind}'.");
    public static ProposalApplyException BackendError(string detail) => new($"Apply failed: {detail}");
}

/// <summary>Maps a proposal <c>kind</c> to its <see cref="IProposalApplier"/>. Thread-safe.</summary>
public sealed class ProposalApplierRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, IProposalApplier> _byKind = new(StringComparer.Ordinal);

    public void Register(IProposalApplier applier)
    {
        lock (_gate) _byKind[applier.Kind] = applier;
    }

    public void Register(IEnumerable<IProposalApplier> appliers)
    {
        lock (_gate)
            foreach (var applier in appliers)
                _byKind[applier.Kind] = applier;
    }

    public IProposalApplier? ApplierFor(string kind)
    {
        lock (_gate) return _byKind.TryGetValue(kind, out var applier) ? applier : null;
    }

    public IReadOnlyList<string> RegisteredKinds()
    {
        lock (_gate) return _byKind.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
    }
}
