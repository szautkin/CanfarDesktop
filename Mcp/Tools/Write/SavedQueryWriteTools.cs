using System.Text.Json;
using CanfarDesktop.Mcp.Tools.Proposals;

namespace CanfarDesktop.Mcp.Tools.Write;

// ─────────────────────────────────────────────────────────────────────────────
// Saved-query write tools + appliers. The tool validates + builds a proposal; the
// applier decodes the proposal payload and invokes an injected store action (so the
// applier stays pure/testable — the WinUI-coupled SavedQuery/store wiring is host-side).
// The Windows SavedQuery is name-keyed {Name, Adql, SavedAt}, so save_query is an
// upsert (covering the macOS save + update tools) and delete is by name.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Proposal payload for <c>save_query</c>.</summary>
public sealed record SaveQueryPayload(string Name, string Adql);

/// <summary>Proposal payload for <c>delete_saved_query</c>.</summary>
public sealed record DeleteSavedQueryPayload(string Name);

/// <summary><c>save_query</c> — propose saving/overwriting a named ADQL query. SemanticWrite.</summary>
public sealed class SaveQueryTool : JsonWriteTool<SaveQueryTool.Args>
{
    public override McpVerbClass VerbClass => McpVerbClass.SemanticWrite;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "save_query",
        "Propose saving a named ADQL query to the user's saved queries (overwrites an existing query with " +
        "the same name). Queues for the user to apply.",
        """{"type":"object","properties":{"name":{"type":"string"},"adql":{"type":"string"}},"required":["name","adql"],"additionalProperties":false}""");

    protected override Task<ProposalPlan> PlanAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var name = (args.Name ?? string.Empty).Trim();
        var adql = (args.Adql ?? string.Empty).Trim();
        if (name.Length == 0) throw new McpToolException(new InvalidArgument("name is required"));
        if (adql.Length == 0) throw new McpToolException(new InvalidArgument("adql is required"));

        return Task.FromResult(ProposalPlan.Encoding("save_query", $"Save query: {name}", new SaveQueryPayload(name, adql)));
    }

    public sealed record Args
    {
        public string? Name { get; init; }
        public string? Adql { get; init; }
    }
}

/// <summary><c>delete_saved_query</c> — propose deleting a saved query by name. Destructive.</summary>
public sealed class DeleteSavedQueryTool : JsonWriteTool<DeleteSavedQueryTool.Args>
{
    public override McpVerbClass VerbClass => McpVerbClass.Destructive;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "delete_saved_query",
        "Propose deleting a saved query by name. Queues for the user to apply (a destructive change).",
        """{"type":"object","properties":{"name":{"type":"string"}},"required":["name"],"additionalProperties":false}""");

    protected override Task<ProposalPlan> PlanAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var name = (args.Name ?? string.Empty).Trim();
        if (name.Length == 0) throw new McpToolException(new InvalidArgument("name is required"));

        return Task.FromResult(ProposalPlan.Encoding("delete_saved_query", $"Delete saved query: {name}", new DeleteSavedQueryPayload(name)));
    }

    public sealed record Args { public string? Name { get; init; } }
}

/// <summary>Applies a <c>save_query</c> proposal by decoding its payload and invoking the host save action.</summary>
public sealed class SaveQueryApplier : IProposalApplier
{
    private readonly Func<SaveQueryPayload, Models.AgentAttribution?, Task> _save;

    public SaveQueryApplier(Func<SaveQueryPayload, Task> save) : this((p, _) => save(p)) { }

    /// <summary>Attribution-aware overload: the stamp the saved entity should carry (null for user origin).</summary>
    public SaveQueryApplier(Func<SaveQueryPayload, Models.AgentAttribution?, Task> save) => _save = save;

    public string Kind => "save_query";

    public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
    {
        var payload = Decode(proposal.Payload);
        return _save(payload, Agents.AgentAttributionStamp.ForProposal(proposal));
    }

    private static SaveQueryPayload Decode(byte[] payload)
        => JsonSerializer.Deserialize<SaveQueryPayload>(payload, McpJson.Options)
           ?? throw ProposalApplyException.BackendError("save_query payload was empty");
}

/// <summary>Applies a <c>delete_saved_query</c> proposal.</summary>
public sealed class DeleteSavedQueryApplier : IProposalApplier
{
    private readonly Func<DeleteSavedQueryPayload, Task> _delete;
    public DeleteSavedQueryApplier(Func<DeleteSavedQueryPayload, Task> delete) => _delete = delete;

    public string Kind => "delete_saved_query";

    public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
    {
        var payload = ProposalPayload.Decode<DeleteSavedQueryPayload>(proposal);
        return _delete(payload);
    }
}
