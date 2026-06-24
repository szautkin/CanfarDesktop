using CanfarDesktop.Mcp.Tools.Proposals;

namespace CanfarDesktop.Mcp.Tools;

/// <summary>
/// Outcome of a tool invocation. The read surface returns <see cref="DataResult"/> (JSON payload),
/// <see cref="FailedResult"/> (typed reason), or <see cref="ImageToolResult"/>. A write tool returns
/// <see cref="ProposedResult"/> (a queued proposal); the router may turn it into a DataResult on
/// auto-apply. Mirrors the macOS ToolResult discriminants.
/// </summary>
public abstract record ToolResult
{
    public static ToolResult Ok(byte[] json) => new DataResult(json);
    public static ToolResult Fail(ToolFailureReason reason) => new FailedResult(reason);
    public static ToolResult ImageResult(byte[] data, string mimeType, string? caption = null)
        => new ImageToolResult(data, mimeType, caption);
    public static ToolResult Proposed(PendingProposal proposal) => new ProposedResult(proposal);
}

public sealed record DataResult(byte[] Json) : ToolResult;
public sealed record FailedResult(ToolFailureReason Reason) : ToolResult;
public sealed record ImageToolResult(byte[] Data, string MimeType, string? Caption) : ToolResult;
public sealed record ProposedResult(PendingProposal Proposal) : ToolResult;

/// <summary>A typed, PII-safe failure reason with a user-facing message + a stable audit tag.</summary>
public abstract record ToolFailureReason
{
    public abstract string Description { get; }
    public abstract string AuditTag { get; }

    protected static string Clip(string s) => s.Length <= 200 ? s : s[..200];
}

public sealed record InvalidArgument(string Detail) : ToolFailureReason
{
    public override string Description => Clip($"Invalid argument: {Detail}");
    public override string AuditTag => "invalid_argument";
}

public sealed record UnknownTarget(string Detail) : ToolFailureReason
{
    public override string Description => Clip($"Unknown target: {Detail}");
    public override string AuditTag => "unknown_target";
}

public sealed record TargetNotResolved(string Detail) : ToolFailureReason
{
    public override string Description => Clip($"Could not resolve: {Detail}");
    public override string AuditTag => "target_not_resolved";
}

public sealed record AuthRequired(string Detail = "Sign in to CADC/CANFAR is required for this tool.") : ToolFailureReason
{
    public override string Description => Clip(Detail);
    public override string AuditTag => "auth_required";
}

public sealed record BackendError(string Detail) : ToolFailureReason
{
    public override string Description => Clip($"Backend error: {Detail}");
    public override string AuditTag => "backend_error";
}

public sealed record NotImplemented(string Detail = "Not implemented") : ToolFailureReason
{
    public override string Description => Clip(Detail);
    public override string AuditTag => "not_implemented";
}

public sealed record UpstreamTimeout : ToolFailureReason
{
    public override string Description => "The upstream service timed out.";
    public override string AuditTag => "timeout";
}

public sealed record ContentTypeMismatch(string Detail) : ToolFailureReason
{
    public override string Description => Clip($"Unexpected content type: {Detail}");
    public override string AuditTag => "content_type_mismatch";
}

public sealed record PreviewNotFound(string Detail) : ToolFailureReason
{
    public override string Description => Clip($"No preview image: {Detail}");
    public override string AuditTag => "preview_not_found";
}

public sealed record PreviewTooLarge(int Bytes) : ToolFailureReason
{
    public override string Description => $"Preview image is too large ({Bytes} bytes) for the response limit.";
    public override string AuditTag => "preview_too_large";
}

public sealed record PerTurnProposalCapExceeded(int Limit) : ToolFailureReason
{
    public override string Description => $"Per-turn proposal cap exceeded (limit {Limit}). Apply or withdraw pending proposals before submitting more.";
    public override string AuditTag => "per_turn_proposal_cap_exceeded";
}

/// <summary>Thrown by a tool's handler to surface a typed failure (mapped to <see cref="FailedResult"/>).</summary>
public sealed class McpToolException : Exception
{
    public ToolFailureReason Reason { get; }
    public McpToolException(ToolFailureReason reason) : base(reason.Description) => Reason = reason;
}
