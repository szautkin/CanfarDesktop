using CanfarDesktop.Mcp.Tools.Proposals;
using CanfarDesktop.Services.AiGuide;

namespace CanfarDesktop.Mcp.Tools.Write;

// ─────────────────────────────────────────────────────────────────────────────
// AI Guide management tools — let the AGENT re-tune its own tool surface: override
// (or reset) a built-in tool's description, and add / update / delete user guide
// tools. All writes go through the proposal/applier gate like any other write; the
// appliers invoke the live AiGuideService (host-side), which the MCP server reads on
// the next tools/list, so an edit re-tunes the manifest live. Windows-ahead of macOS
// (there the AI Guide is UI-only).
// ─────────────────────────────────────────────────────────────────────────────

public sealed record SetToolDescriptionPayload(string ToolName, string Description);
public sealed record ClearToolDescriptionPayload(string ToolName);
public sealed record AddGuideToolPayload(string Name, string Description, string? Body);
public sealed record UpdateGuideToolPayload(string Id, string Name, string Description, string? Body);
public sealed record DeleteGuideToolPayload(string Id);

/// <summary><c>set_tool_description</c> — override the description another tool advertises. SemanticWrite.</summary>
public sealed class SetToolDescriptionTool : JsonWriteTool<SetToolDescriptionTool.Args>
{
    public override McpVerbClass VerbClass => McpVerbClass.SemanticWrite;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "set_tool_description",
        "Override the description another MCP tool advertises in tools/list — re-tune how you read it. " +
        "Pass the exact toolName (from tools/list) and the new description (max 600 chars). Use " +
        "clear_tool_description to revert to the built-in text. Gated like other writes.",
        """{"type":"object","properties":{"toolName":{"type":"string"},"description":{"type":"string"}},"required":["toolName","description"],"additionalProperties":false}""");

    protected override Task<ProposalPlan> PlanAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var name = (args.ToolName ?? string.Empty).Trim();
        var desc = (args.Description ?? string.Empty).Trim();
        if (name.Length == 0) throw new McpToolException(new InvalidArgument("toolName is required"));
        if (desc.Length == 0) throw new McpToolException(new InvalidArgument("description is required (use clear_tool_description to reset)"));
        if (desc.Length > AiGuideService.MaxDescriptionChars)
            throw new McpToolException(new InvalidArgument($"description exceeds {AiGuideService.MaxDescriptionChars} characters"));

        return Task.FromResult(ProposalPlan.Encoding("set_tool_description",
            $"Override the description of {name}", new SetToolDescriptionPayload(name, desc)));
    }

    public sealed record Args
    {
        public string? ToolName { get; init; }
        public string? Description { get; init; }
    }
}

/// <summary><c>clear_tool_description</c> — revert a tool to its built-in description. SemanticWrite.</summary>
public sealed class ClearToolDescriptionTool : JsonWriteTool<ClearToolDescriptionTool.Args>
{
    public override McpVerbClass VerbClass => McpVerbClass.SemanticWrite;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "clear_tool_description",
        "Revert a tool's description to its built-in default, removing any override set via " +
        "set_tool_description. Pass the exact toolName (from tools/list).",
        """{"type":"object","properties":{"toolName":{"type":"string"}},"required":["toolName"],"additionalProperties":false}""");

    protected override Task<ProposalPlan> PlanAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var name = (args.ToolName ?? string.Empty).Trim();
        if (name.Length == 0) throw new McpToolException(new InvalidArgument("toolName is required"));

        return Task.FromResult(ProposalPlan.Encoding("clear_tool_description",
            $"Reset {name} to its built-in description", new ClearToolDescriptionPayload(name)));
    }

    public sealed record Args { public string? ToolName { get; init; } }
}

/// <summary><c>add_guide_tool</c> — create a user guide tool the agent can call for instructions. SemanticWrite.</summary>
public sealed class AddGuideToolTool : JsonWriteTool<AddGuideToolTool.Args>
{
    public override McpVerbClass VerbClass => McpVerbClass.SemanticWrite;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "add_guide_tool",
        "Create a new read-only \"guide\" tool that you (and the user) can call to get stored instructions. " +
        "Pass name (slugged to a wire-safe tool name), a one-line description (shown in tools/list, max 600 " +
        "chars), and an optional body (the text returned when the tool is called, max 4000 chars). The new " +
        "tool appears in tools/list after it applies. Gated like other writes.",
        """{"type":"object","properties":{"name":{"type":"string"},"description":{"type":"string"},"body":{"type":"string"}},"required":["name","description"],"additionalProperties":false}""");

    protected override Task<ProposalPlan> PlanAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var name = (args.Name ?? string.Empty).Trim();
        var desc = (args.Description ?? string.Empty).Trim();
        if (AiGuideService.Slug(name).Length == 0)
            throw new McpToolException(new InvalidArgument("name must contain letters or numbers"));
        if (desc.Length == 0) throw new McpToolException(new InvalidArgument("description is required"));
        if (desc.Length > AiGuideService.MaxDescriptionChars)
            throw new McpToolException(new InvalidArgument($"description exceeds {AiGuideService.MaxDescriptionChars} characters"));
        if (args.Body is { } body && body.Trim().Length > AiGuideService.MaxBodyChars)
            throw new McpToolException(new InvalidArgument($"body exceeds {AiGuideService.MaxBodyChars} characters"));

        return Task.FromResult(ProposalPlan.Encoding("add_guide_tool",
            $"Add guide tool: {AiGuideService.Slug(name)}", new AddGuideToolPayload(name, desc, args.Body)));
    }

    public sealed record Args
    {
        public string? Name { get; init; }
        public string? Description { get; init; }
        public string? Body { get; init; }
    }
}

/// <summary><c>update_guide_tool</c> — edit an existing guide tool by id. SemanticWrite.</summary>
public sealed class UpdateGuideToolTool : JsonWriteTool<UpdateGuideToolTool.Args>
{
    public override McpVerbClass VerbClass => McpVerbClass.SemanticWrite;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "update_guide_tool",
        "Update an existing guide tool by id (from list_guide_tools): its name, description, and/or body. " +
        "Same limits as add_guide_tool. Gated like other writes.",
        """{"type":"object","properties":{"id":{"type":"string"},"name":{"type":"string"},"description":{"type":"string"},"body":{"type":"string"}},"required":["id","name","description"],"additionalProperties":false}""");

    protected override Task<ProposalPlan> PlanAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var id = (args.Id ?? string.Empty).Trim();
        var name = (args.Name ?? string.Empty).Trim();
        var desc = (args.Description ?? string.Empty).Trim();
        if (!Guid.TryParse(id, out _)) throw new McpToolException(new InvalidArgument("id must be a guide tool id from list_guide_tools"));
        if (AiGuideService.Slug(name).Length == 0)
            throw new McpToolException(new InvalidArgument("name must contain letters or numbers"));
        if (desc.Length == 0) throw new McpToolException(new InvalidArgument("description is required"));
        if (desc.Length > AiGuideService.MaxDescriptionChars)
            throw new McpToolException(new InvalidArgument($"description exceeds {AiGuideService.MaxDescriptionChars} characters"));
        if (args.Body is { } body && body.Trim().Length > AiGuideService.MaxBodyChars)
            throw new McpToolException(new InvalidArgument($"body exceeds {AiGuideService.MaxBodyChars} characters"));

        return Task.FromResult(ProposalPlan.Encoding("update_guide_tool",
            $"Update guide tool: {AiGuideService.Slug(name)}", new UpdateGuideToolPayload(id, name, desc, args.Body)));
    }

    public sealed record Args
    {
        public string? Id { get; init; }
        public string? Name { get; init; }
        public string? Description { get; init; }
        public string? Body { get; init; }
    }
}

/// <summary><c>delete_guide_tool</c> — remove a guide tool by id. Destructive.</summary>
public sealed class DeleteGuideToolTool : JsonWriteTool<DeleteGuideToolTool.Args>
{
    public override McpVerbClass VerbClass => McpVerbClass.Destructive;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "delete_guide_tool",
        "Delete a guide tool by id (from list_guide_tools). Destructive — queues for the user to apply " +
        "unless auto-apply is on.",
        """{"type":"object","properties":{"id":{"type":"string"}},"required":["id"],"additionalProperties":false}""");

    protected override Task<ProposalPlan> PlanAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var id = (args.Id ?? string.Empty).Trim();
        if (!Guid.TryParse(id, out _)) throw new McpToolException(new InvalidArgument("id must be a guide tool id from list_guide_tools"));

        return Task.FromResult(ProposalPlan.Encoding("delete_guide_tool",
            $"Delete guide tool {id}", new DeleteGuideToolPayload(id)));
    }

    public sealed record Args { public string? Id { get; init; } }
}

/// <summary><c>list_guide_tools</c> — list the user-authored guide tools (so the agent can edit/delete by id).</summary>
public sealed class ListGuideToolsTool : JsonReadTool<EmptyArgs, ListGuideToolsTool.Output>
{
    private readonly Func<IReadOnlyList<AiGuideToolEntry>> _guides;
    public ListGuideToolsTool(Func<IReadOnlyList<AiGuideToolEntry>> guides) => _guides = guides;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "list_guide_tools",
        "List the user-authored guide tools (id, name, description, whether it has a body), so you can " +
        "update_guide_tool or delete_guide_tool one by id.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override Task<Output> HandleAsync(EmptyArgs args, McpToolContext context, CancellationToken ct)
    {
        var items = _guides()
            .Select(g => new GuideView(g.Id.ToString(), g.Name, g.Description, !string.IsNullOrWhiteSpace(g.Body)))
            .ToList();
        return Task.FromResult(new Output(items.Count, items));
    }

    public sealed record GuideView(string Id, string Name, string Description, bool HasBody);
    public sealed record Output(int Count, IReadOnlyList<GuideView> Guides);
}

// ── Appliers (decode the payload + invoke an injected AiGuideService action) ──────────────────

public sealed class SetToolDescriptionApplier : IProposalApplier
{
    private readonly Func<SetToolDescriptionPayload, Task> _apply;
    public SetToolDescriptionApplier(Func<SetToolDescriptionPayload, Task> apply) => _apply = apply;
    public string Kind => "set_tool_description";
    public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
        => _apply(ProposalPayload.Decode<SetToolDescriptionPayload>(proposal));
}

public sealed class ClearToolDescriptionApplier : IProposalApplier
{
    private readonly Func<ClearToolDescriptionPayload, Task> _apply;
    public ClearToolDescriptionApplier(Func<ClearToolDescriptionPayload, Task> apply) => _apply = apply;
    public string Kind => "clear_tool_description";
    public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
        => _apply(ProposalPayload.Decode<ClearToolDescriptionPayload>(proposal));
}

public sealed class AddGuideToolApplier : IProposalApplier
{
    private readonly Func<AddGuideToolPayload, Task> _apply;
    public AddGuideToolApplier(Func<AddGuideToolPayload, Task> apply) => _apply = apply;
    public string Kind => "add_guide_tool";
    public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
        => _apply(ProposalPayload.Decode<AddGuideToolPayload>(proposal));
}

public sealed class UpdateGuideToolApplier : IProposalApplier
{
    private readonly Func<UpdateGuideToolPayload, Task> _apply;
    public UpdateGuideToolApplier(Func<UpdateGuideToolPayload, Task> apply) => _apply = apply;
    public string Kind => "update_guide_tool";
    public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
        => _apply(ProposalPayload.Decode<UpdateGuideToolPayload>(proposal));
}

public sealed class DeleteGuideToolApplier : IProposalApplier
{
    private readonly Func<DeleteGuideToolPayload, Task> _apply;
    public DeleteGuideToolApplier(Func<DeleteGuideToolPayload, Task> apply) => _apply = apply;
    public string Kind => "delete_guide_tool";
    public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
        => _apply(ProposalPayload.Decode<DeleteGuideToolPayload>(proposal));
}
