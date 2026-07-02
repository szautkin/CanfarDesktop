using System.Text.Json;
using CanfarDesktop.Models;
using CanfarDesktop.Mcp.Tools.Proposals;

namespace CanfarDesktop.Mcp.Tools.Write;

// ─────────────────────────────────────────────────────────────────────────────
// Observation-note write tools + appliers. Notes are keyed by publisher id; an
// update merges the provided fields over the existing note (omitted fields keep
// their current value). The merge is a pure helper (testable); the applier decodes
// the payload and invokes the injected store action.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Proposal payload for one note update (omitted fields keep the existing value on apply).</summary>
public sealed record UpdateObservationNotePayload(string PublisherId, string? Text, int? Rating, IReadOnlyList<string>? Tags);

/// <summary>Proposal payload for a batch note update.</summary>
public sealed record BulkUpdateObservationNotesPayload(IReadOnlyList<UpdateObservationNotePayload> Items);

/// <summary>Pure merge of a note-update payload over an existing note. Provided fields win; the rest persist.</summary>
public static class ObservationNoteMerge
{
    public static ObservationNote Apply(ObservationNote? existing, UpdateObservationNotePayload payload, DateTimeOffset now)
        => new()
        {
            PublisherID = payload.PublisherId,
            Note = payload.Text ?? existing?.Note ?? string.Empty,
            Rating = payload.Rating ?? existing?.Rating ?? 0,
            Tags = payload.Tags ?? existing?.Tags ?? Array.Empty<string>(),
            UpdatedUtc = now,
        };
}

internal static class NoteWriteHelpers
{
    public static UpdateObservationNotePayload Validate(string? publisherId, string? text, int? rating, IReadOnlyList<string>? tags)
    {
        var pid = (publisherId ?? string.Empty).Trim();
        if (pid.Length == 0) throw new McpToolException(new InvalidArgument("publisherId is required"));
        if (rating is < 0 or > 5) throw new McpToolException(new InvalidArgument("rating must be between 0 and 5"));
        return new UpdateObservationNotePayload(pid, text, rating, tags);
    }

    public static string Clip(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}

/// <summary>Args for a single note update (also the item shape for the bulk tool).</summary>
public sealed record NoteUpdateArgs
{
    public string? PublisherId { get; init; }
    public string? Text { get; init; }
    public int? Rating { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
}

/// <summary><c>update_observation_note</c> — propose updating one observation's research note. SemanticWrite.</summary>
public sealed class UpdateObservationNoteTool : JsonWriteTool<NoteUpdateArgs>
{
    public override McpVerbClass VerbClass => McpVerbClass.SemanticWrite;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "update_observation_note",
        "Propose updating the research note for a downloaded observation (by publisher id): set the note " +
        "text, a 0–5 rating, and/or tags. Omitted fields keep their current value. Queues for the user to apply.",
        """{"type":"object","properties":{"publisherId":{"type":"string"},"text":{"type":"string"},"rating":{"type":"integer","minimum":0,"maximum":5},"tags":{"type":"array","items":{"type":"string"}}},"required":["publisherId"],"additionalProperties":false}""");

    protected override Task<ProposalPlan> PlanAsync(NoteUpdateArgs args, McpToolContext context, CancellationToken ct)
    {
        var payload = NoteWriteHelpers.Validate(args.PublisherId, args.Text, args.Rating, args.Tags);
        var preview = payload.Text is { Length: > 0 } t ? $": {NoteWriteHelpers.Clip(t, 40)}" : string.Empty;
        return Task.FromResult(ProposalPlan.Encoding("update_observation_note", $"Update note on {payload.PublisherId}{preview}", payload));
    }
}

/// <summary><c>bulk_update_observation_notes</c> — propose updating up to 50 notes at once. SemanticWrite.</summary>
public sealed class BulkUpdateObservationNotesTool : JsonWriteTool<BulkUpdateObservationNotesTool.Args>
{
    public const int MaxItems = 50;

    public override McpVerbClass VerbClass => McpVerbClass.SemanticWrite;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "bulk_update_observation_notes",
        "Propose updating multiple observation notes at once (1–50 items, applied all-or-nothing). Each item " +
        "is the same shape as update_observation_note. Queues for the user to apply.",
        """{"type":"object","properties":{"items":{"type":"array","minItems":1,"maxItems":50,"items":{"type":"object","properties":{"publisherId":{"type":"string"},"text":{"type":"string"},"rating":{"type":"integer","minimum":0,"maximum":5},"tags":{"type":"array","items":{"type":"string"}}},"required":["publisherId"],"additionalProperties":false}}},"required":["items"],"additionalProperties":false}""");

    protected override Task<ProposalPlan> PlanAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var items = args.Items ?? Array.Empty<NoteUpdateArgs>();
        if (items.Count == 0) throw new McpToolException(new InvalidArgument("items must have at least one entry"));
        if (items.Count > MaxItems) throw new McpToolException(new InvalidArgument($"items cannot exceed {MaxItems}"));

        var payloads = items.Select(i => NoteWriteHelpers.Validate(i.PublisherId, i.Text, i.Rating, i.Tags)).ToList();
        return Task.FromResult(ProposalPlan.Encoding(
            "bulk_update_observation_notes", $"Update notes on {payloads.Count} observation(s)", new BulkUpdateObservationNotesPayload(payloads)));
    }

    public sealed record Args { public IReadOnlyList<NoteUpdateArgs>? Items { get; init; } }
}

/// <summary>Applies an <c>update_observation_note</c> proposal via the injected store action.</summary>
public sealed class UpdateObservationNoteApplier : IProposalApplier
{
    private readonly Func<UpdateObservationNotePayload, Models.AgentAttribution?, Task> _apply;

    public UpdateObservationNoteApplier(Func<UpdateObservationNotePayload, Task> apply) : this((p, _) => apply(p)) { }

    /// <summary>Attribution-aware overload: the stamp the written note should carry (null for user origin).</summary>
    public UpdateObservationNoteApplier(Func<UpdateObservationNotePayload, Models.AgentAttribution?, Task> apply) => _apply = apply;

    public string Kind => "update_observation_note";

    public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
        => _apply(ProposalPayload.Decode<UpdateObservationNotePayload>(proposal), Agents.AgentAttributionStamp.ForProposal(proposal));
}

/// <summary>Applies a <c>bulk_update_observation_notes</c> proposal (all items).</summary>
public sealed class BulkUpdateObservationNotesApplier : IProposalApplier
{
    private readonly Func<IReadOnlyList<UpdateObservationNotePayload>, Models.AgentAttribution?, Task> _apply;

    public BulkUpdateObservationNotesApplier(Func<IReadOnlyList<UpdateObservationNotePayload>, Task> apply)
        : this((p, _) => apply(p)) { }

    /// <summary>Attribution-aware overload: the stamp every written note should carry (null for user origin).</summary>
    public BulkUpdateObservationNotesApplier(Func<IReadOnlyList<UpdateObservationNotePayload>, Models.AgentAttribution?, Task> apply)
        => _apply = apply;

    public string Kind => "bulk_update_observation_notes";

    public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
    {
        var payload = ProposalPayload.Decode<BulkUpdateObservationNotesPayload>(proposal);
        return _apply(payload.Items, Agents.AgentAttributionStamp.ForProposal(proposal));
    }
}
