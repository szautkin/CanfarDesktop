namespace CanfarDesktop.Mcp;

/// <summary>One external client the MCP server has seen connect (identified by its self-reported
/// <c>name/version</c>). Identity is attribution-only — the real boundary is the per-user pipe ACL — so
/// this drives visibility + opt-in lockdown + revocation, not authentication.</summary>
public sealed record McpSeenClient(string ClientId, DateTimeOffset FirstSeen, DateTimeOffset LastSeen, int ConnectCount);

/// <summary>Persistence seam for <see cref="McpClientApprovalStore"/> — split out so the gate logic is
/// unit-testable without any platform storage. The real impl is <c>LocalSettingsApprovalStorage</c>.</summary>
public interface IMcpApprovalStorage
{
    /// <summary>When true, only approved clients may connect; default false (allow all — back-compat).</summary>
    bool RequireApproval { get; set; }
    HashSet<string> LoadApproved();
    void SaveApproved(IReadOnlyCollection<string> approved);
}

/// <summary>
/// The wired MCP approval gate: records every client that connects, and — when the user turns on
/// "require approval" — permits only clients on the persisted allow-list (others get
/// <c>SessionNotApproved</c> at initialize until the user approves them). Default policy is allow-all so
/// existing setups are unchanged. The app's own loopback self-test client is always permitted so the
/// connection wizard's Verify step works under any policy. Thread-safe (connections dispatch concurrently).
/// </summary>
public sealed class McpClientApprovalStore : IApprovalGate
{
    private readonly IMcpApprovalStorage _storage;
    private readonly Func<DateTimeOffset> _now;
    private readonly object _lock = new();
    private readonly Dictionary<string, McpSeenClient> _seen = new(StringComparer.Ordinal);
    private readonly HashSet<string> _approved;

    public McpClientApprovalStore(IMcpApprovalStorage storage, Func<DateTimeOffset>? now = null)
    {
        _storage = storage;
        _approved = storage.LoadApproved();
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>When true, only approved clients may connect. Persisted.</summary>
    public bool RequireApproval
    {
        get => _storage.RequireApproval;
        set => _storage.RequireApproval = value;
    }

    public Task<bool> PermitAsync(string clientId)
    {
        // The app's own self-test probe is internal, not an external agent: always allow, never list it.
        if (IsInternalClient(clientId)) return Task.FromResult(true);

        lock (_lock)
        {
            var approved = !_storage.RequireApproval || _approved.Contains(clientId);
            var now = _now();
            _seen[clientId] = _seen.TryGetValue(clientId, out var prev)
                ? prev with { LastSeen = now, ConnectCount = prev.ConnectCount + 1 }
                : new McpSeenClient(clientId, now, now, 1);
            return Task.FromResult(approved);
        }
    }

    /// <summary>Clients that have connected this session, most-recent first.</summary>
    public IReadOnlyList<McpSeenClient> SeenClients()
    {
        lock (_lock) return _seen.Values.OrderByDescending(c => c.LastSeen).ToList();
    }

    public bool IsApproved(string clientId)
    {
        lock (_lock) return _approved.Contains(clientId);
    }

    public void Approve(string clientId)
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(clientId) || !_approved.Add(clientId)) return;
            _storage.SaveApproved(_approved);
        }
    }

    public void Revoke(string clientId)
    {
        lock (_lock)
        {
            if (!_approved.Remove(clientId)) return;
            _storage.SaveApproved(_approved);
        }
    }

    public static bool IsInternalClient(string clientId) =>
        clientId == McpConstants.SelfTestClientName
        || clientId.StartsWith(McpConstants.SelfTestClientName + "/", StringComparison.Ordinal);
}
