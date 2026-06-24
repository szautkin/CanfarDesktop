using System.Text.Json;

namespace CanfarDesktop.Mcp.Tools.Proposals;

/// <summary>Lifecycle state of a write proposal. 1-to-1 with the macOS ProposalState.</summary>
public enum ProposalState
{
    Pending,
    Applied,
    Rejected,
    Withdrawn,
    Unknown,
}

/// <summary>
/// A queued write the user (or auto-apply) must accept before it mutates real state. The payload is
/// opaque JSON bytes the matching <see cref="IProposalApplier"/> decodes on apply. 1-to-1 with the
/// macOS PendingProposal. <see cref="Kind"/> is the stable applier-routing key (may differ from
/// <see cref="ToolName"/> for bulk tools); <see cref="RequestId"/> links back to the originating call.
/// </summary>
public sealed record PendingProposal(
    Guid Id,
    string ToolName,
    string Kind,
    string Summary,
    byte[] Payload,
    DateTimeOffset CreatedAt,
    OperationOrigin Origin,
    Guid? RequestId)
{
    /// <summary>Create a fresh proposal with a new id + creation timestamp.</summary>
    public static PendingProposal Create(
        string toolName, string kind, string summary, byte[] payload, OperationOrigin origin, Guid? requestId = null)
        => new(Guid.NewGuid(), toolName, kind, summary, payload, DateTimeOffset.UtcNow, origin, requestId);
}

/// <summary>
/// What a <c>JsonWriteTool.Plan</c> returns: the applier-routing kind, a human summary, and the encoded
/// payload. The base tool turns this into a <see cref="PendingProposal"/>. 1-to-1 with macOS ProposalPlan.
/// </summary>
public sealed record ProposalPlan(string Kind, string Summary, byte[] Payload)
{
    /// <summary>Build a plan by JSON-encoding a typed payload with the shared MCP options.</summary>
    public static ProposalPlan Encoding<T>(string kind, string summary, T payload)
        => new(kind, summary, JsonSerializer.SerializeToUtf8Bytes(payload, McpJson.Options));
}
