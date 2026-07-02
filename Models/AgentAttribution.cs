namespace CanfarDesktop.Models;

/// <summary>
/// Provenance stamp left on any persistent entity that originated from an MCP-connected AI agent.
/// Carried inline on SavedQuery, ObservationNote, DownloadedObservation. Null means the user authored
/// the entity directly; the UI shows a small wand badge whenever it is non-null. Contains only the
/// compact metadata the audit log already stores (fingerprint, client label, no payloads).
/// 1-to-1 with the macOS AgentAttribution.
/// </summary>
public sealed record AgentAttribution(
    Guid ProposalId,
    string OriginFingerprint,
    string OriginLabel,
    DateTimeOffset AppliedAt,
    string Summary);
