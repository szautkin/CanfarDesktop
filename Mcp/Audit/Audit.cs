using CanfarDesktop.Mcp.Tools;

namespace CanfarDesktop.Mcp.Audit;

public enum AuditOutcome { Success, Failed, Rejected, Unknown }

/// <summary>
/// One PII-safe audit record per tool dispatch. The raw arguments are NEVER logged — only a SHA-256
/// payload hash — so credentials/queries can't leak into the audit trail.
/// </summary>
public sealed record AuditEntry(
    Guid RequestId,
    DateTimeOffset Timestamp,
    string OriginLabel,
    string ToolName,
    McpVerbClass VerbClass,
    AuditOutcome Outcome,
    long DurationMs,
    string PayloadHash)
{
    public string Line()
        => $"{Timestamp:O} [{OriginLabel}] {ToolName} ({VerbClass}) -> {Outcome} {DurationMs}ms #{PayloadHash[..Math.Min(8, PayloadHash.Length)]}";
}

public interface IAuditSink
{
    void Record(AuditEntry entry);
}

/// <summary>In-memory audit sink (tests + an in-app activity ring).</summary>
public sealed class CapturingAuditSink : IAuditSink
{
    private readonly object _gate = new();
    private readonly List<AuditEntry> _entries = new();

    public void Record(AuditEntry entry)
    {
        lock (_gate) _entries.Add(entry);
    }

    public IReadOnlyList<AuditEntry> Entries
    {
        get { lock (_gate) return _entries.ToList(); }
    }
}

/// <summary>Writes audit lines to the debugger output.</summary>
public sealed class LoggingAuditSink : IAuditSink
{
    public void Record(AuditEntry entry) => System.Diagnostics.Debug.WriteLine("[mcp-audit] " + entry.Line());
}
