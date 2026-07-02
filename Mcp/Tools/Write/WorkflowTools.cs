using CanfarDesktop.Mcp.Tools.Proposals;
using CanfarDesktop.Services.Workflows;

namespace CanfarDesktop.Mcp.Tools.Write;

// ─────────────────────────────────────────────────────────────────────────────
// Workflow tools: research protocols the agent can read, follow, author, and
// check off. save/update/set_step/use are SemanticWrite (auto-apply under the
// user's setting); delete_workflow is Destructive and always queues. Check-off
// state only exists on LOCAL workflows — templates/VOSpace sources must be
// instantiated with use_workflow first (the store's errors say exactly that).
// ─────────────────────────────────────────────────────────────────────────────

public sealed record SaveWorkflowPayload(string Name, string Text, string Location);
public sealed record UpdateWorkflowPayload(string Id, string Text);
public sealed record SetWorkflowStepPayload(string Id, int Index, bool Done);
public sealed record UseWorkflowPayload(string Id, string? Name);
public sealed record DeleteWorkflowPayload(string Id);

/// <summary>Wire shape for one workflow step (mirrors WorkflowStep, plus nothing agent-hostile).</summary>
public sealed record WorkflowStepWire(int Index, string Title, string Body, IReadOnlyList<string> Tools, string? View, string? Note, bool Done);

public sealed record WorkflowSummaryWire(string Id, string Title, string Description, IReadOnlyList<string> Tags, string Source, int DoneSteps, int TotalSteps);

public sealed record WorkflowWire(
    string Id, string Title, string Description, IReadOnlyList<string> Tags, string Source,
    int DoneSteps, int TotalSteps, IReadOnlyList<WorkflowStepWire> Steps, string RawText)
{
    public static WorkflowWire From(WorkflowInfo w) => new(
        w.Id, w.Doc.Title, w.Doc.Description, w.Doc.Tags, w.Source.ToString(),
        w.Doc.DoneCount, w.Doc.Steps.Count,
        w.Doc.Steps.Select(s => new WorkflowStepWire(s.Index, s.Title, s.Body, s.Tools, s.View, s.Note, s.Done)).ToList(),
        w.RawText);
}

/// <summary><c>list_workflows</c> — built-in templates + the user's local working copies.</summary>
public sealed class ListWorkflowsTool : JsonReadTool<ListWorkflowsTool.Args, ListWorkflowsTool.Output>
{
    private readonly WorkflowStore _store;
    public ListWorkflowsTool(WorkflowStore store) => _store = store;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "list_workflows",
        "List the user's research workflows: built-in templates (read-only protocols for classic " +
        "CADC/CANFAR research tasks) and their local working copies (which carry step check-off " +
        "progress). Read one with get_workflow; instantiate a template with use_workflow.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override Task<Output> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var items = _store.ListBuiltIn().Concat(_store.ListLocal())
            .Select(w => new WorkflowSummaryWire(
                w.Id, w.Doc.Title, w.Doc.Description, w.Doc.Tags, w.Source.ToString(), w.Doc.DoneCount, w.Doc.Steps.Count))
            .ToList();
        return Task.FromResult(new Output(items.Count, items));
    }

    public sealed record Args { }
    public sealed record Output(int Count, IReadOnlyList<WorkflowSummaryWire> Workflows);
}

/// <summary><c>get_workflow</c> — full structured steps + progress + raw text of one workflow.</summary>
public sealed class GetWorkflowTool : JsonReadTool<GetWorkflowTool.Args, WorkflowWire>
{
    private readonly WorkflowStore _store;
    public GetWorkflowTool(WorkflowStore store) => _store = store;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_workflow",
        "Read one workflow: metadata plus every step (index, title, body, the agent tools the step " +
        "uses, the app view it belongs to, done flag). To follow a workflow: do the steps in order " +
        "with the named tools and mark each with set_workflow_step. Ids come from list_workflows.",
        """{"type":"object","properties":{"id":{"type":"string","description":"builtin:… or local:… id from list_workflows"}},"required":["id"],"additionalProperties":false}""");

    protected override Task<WorkflowWire> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var id = (args.Id ?? string.Empty).Trim();
        if (id.Length == 0) throw new McpToolException(new InvalidArgument("id is required"));
        var info = _store.Get(id)
            ?? throw new McpToolException(new UnknownTarget($"no workflow '{id}' — call list_workflows for ids"));
        return Task.FromResult(WorkflowWire.From(info));
    }

    public sealed record Args { public string? Id { get; init; } }
}

/// <summary><c>save_workflow</c> — CREATE a new workflow from raw `.workflow.md` text.</summary>
public sealed class SaveWorkflowTool : JsonWriteTool<SaveWorkflowTool.Args>
{
    public override McpVerbClass VerbClass => McpVerbClass.SemanticWrite;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "save_workflow",
        "Create a NEW workflow from markdown-checklist text (e.g. turn the current conversation's plan " +
        "into a reusable protocol). Format: `# Title`, `> description`, `Tags: a, b`, then steps as " +
        "`- [ ] **Step title** — what to do` with optional indented `Tool: name1, name2`, `View: search`, " +
        "`Note: hint` lines. location `local` (default; progress-trackable) or `vospace` " +
        "(publishes to vos:<user>/workflows/, shareable). Auto-applies under the user's auto-apply setting.",
        """{"type":"object","properties":{"name":{"type":"string","minLength":1},"text":{"type":"string","minLength":1,"description":"Full .workflow.md content"},"location":{"type":"string","enum":["local","vospace"],"description":"Default local"}},"required":["name","text"],"additionalProperties":false}""");

    protected override Task<ProposalPlan> PlanAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var name = (args.Name ?? string.Empty).Trim();
        var text = args.Text ?? string.Empty;
        if (name.Length == 0) throw new McpToolException(new InvalidArgument("name is required"));
        if (text.Trim().Length == 0) throw new McpToolException(new InvalidArgument("text is required"));

        var doc = WorkflowFormat.Parse(text);
        if (doc.Steps.Count == 0)
            throw new McpToolException(new InvalidArgument(
                "the text has no steps — steps are `- [ ] **Step title** — description` lines"));

        var location = string.Equals(args.Location, "vospace", StringComparison.OrdinalIgnoreCase) ? "vospace" : "local";
        var summary = $"Save workflow \"{name}\" ({doc.Steps.Count} steps) to {(location == "vospace" ? "VOSpace" : "this PC")}";
        return Task.FromResult(ProposalPlan.Encoding("save_workflow", summary, new SaveWorkflowPayload(name, text, location)));
    }

    public sealed record Args { public string? Name { get; init; } public string? Text { get; init; } public string? Location { get; init; } }
}

/// <summary><c>update_workflow</c> — replace a LOCAL workflow's full text.</summary>
public sealed class UpdateWorkflowTool : JsonWriteTool<UpdateWorkflowTool.Args>
{
    public override McpVerbClass VerbClass => McpVerbClass.SemanticWrite;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "update_workflow",
        "Replace the full text of a LOCAL workflow (refine a protocol). Built-in templates are " +
        "read-only — use_workflow them first. Auto-applies under the user's auto-apply setting.",
        """{"type":"object","properties":{"id":{"type":"string","description":"local:… id"},"text":{"type":"string","minLength":1}},"required":["id","text"],"additionalProperties":false}""");

    protected override Task<ProposalPlan> PlanAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var id = (args.Id ?? string.Empty).Trim();
        if (!id.StartsWith(WorkflowStore.LocalPrefix, StringComparison.Ordinal))
            throw new McpToolException(new InvalidArgument("id must be a local:… workflow (templates are read-only — use_workflow first)"));
        if (string.IsNullOrWhiteSpace(args.Text)) throw new McpToolException(new InvalidArgument("text is required"));
        var doc = WorkflowFormat.Parse(args.Text!);
        return Task.FromResult(ProposalPlan.Encoding(
            "update_workflow", $"Update workflow {id} (\"{doc.Title}\", {doc.Steps.Count} steps)", new UpdateWorkflowPayload(id, args.Text!)));
    }

    public sealed record Args { public string? Id { get; init; } public string? Text { get; init; } }
}

/// <summary><c>set_workflow_step</c> — check a step off (or back on) in a LOCAL workflow.</summary>
public sealed class SetWorkflowStepTool : JsonWriteTool<SetWorkflowStepTool.Args>
{
    public override McpVerbClass VerbClass => McpVerbClass.SemanticWrite;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "set_workflow_step",
        "Mark a step of a LOCAL workflow done (or not done) by its 0-based index — call this after " +
        "completing each step so the user sees live progress. Auto-applies under the user's auto-apply setting.",
        """{"type":"object","properties":{"id":{"type":"string","description":"local:… id"},"index":{"type":"integer","minimum":0},"done":{"type":"boolean","description":"Default true"}},"required":["id","index"],"additionalProperties":false}""");

    protected override Task<ProposalPlan> PlanAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var id = (args.Id ?? string.Empty).Trim();
        if (id.Length == 0) throw new McpToolException(new InvalidArgument("id is required"));
        if (args.Index is null || args.Index < 0) throw new McpToolException(new InvalidArgument("index must be >= 0"));
        var done = args.Done ?? true;
        return Task.FromResult(ProposalPlan.Encoding(
            "set_workflow_step",
            $"Mark workflow {id} step {args.Index + 1} {(done ? "done" : "not done")}",
            new SetWorkflowStepPayload(id, args.Index.Value, done)));
    }

    public sealed record Args { public string? Id { get; init; } public int? Index { get; init; } public bool? Done { get; init; } }
}

/// <summary><c>use_workflow</c> — instantiate a template as a LOCAL working copy.</summary>
public sealed class UseWorkflowTool : JsonWriteTool<UseWorkflowTool.Args>
{
    public override McpVerbClass VerbClass => McpVerbClass.SemanticWrite;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "use_workflow",
        "Copy a built-in template (or another workflow) into a LOCAL working copy that can track " +
        "step progress. Optionally name the copy after the concrete target (e.g. \"M31 run\"). " +
        "Auto-applies under the user's auto-apply setting.",
        """{"type":"object","properties":{"id":{"type":"string","description":"builtin:… or local:… source id"},"name":{"type":"string","description":"Optional name for the copy"}},"required":["id"],"additionalProperties":false}""");

    protected override Task<ProposalPlan> PlanAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var id = (args.Id ?? string.Empty).Trim();
        if (id.Length == 0) throw new McpToolException(new InvalidArgument("id is required"));
        return Task.FromResult(ProposalPlan.Encoding(
            "use_workflow", $"Start working copy of workflow {id}" + (args.Name is { Length: > 0 } n ? $" as \"{n}\"" : ""),
            new UseWorkflowPayload(id, args.Name)));
    }

    public sealed record Args { public string? Id { get; init; } public string? Name { get; init; } }
}

/// <summary><c>delete_workflow</c> — delete a LOCAL workflow (Destructive: always queues).</summary>
public sealed class DeleteWorkflowTool : JsonWriteTool<DeleteWorkflowTool.Args>
{
    public override McpVerbClass VerbClass => McpVerbClass.Destructive;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "delete_workflow",
        "Delete a LOCAL workflow file, including its progress. Queues for the user's approval.",
        """{"type":"object","properties":{"id":{"type":"string","description":"local:… id"}},"required":["id"],"additionalProperties":false}""");

    protected override Task<ProposalPlan> PlanAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var id = (args.Id ?? string.Empty).Trim();
        if (!id.StartsWith(WorkflowStore.LocalPrefix, StringComparison.Ordinal))
            throw new McpToolException(new InvalidArgument("id must be a local:… workflow (templates cannot be deleted)"));
        return Task.FromResult(ProposalPlan.Encoding("delete_workflow", $"Delete workflow {id}", new DeleteWorkflowPayload(id)));
    }

    public sealed record Args { public string? Id { get; init; } }
}

// ── Appliers ──────────────────────────────────────────────────────────────────

public sealed class SaveWorkflowApplier : IProposalApplier
{
    private readonly WorkflowStore _store;
    private readonly Func<string, string, CancellationToken, Task> _publishToVoSpace; // (fileName, text)

    public SaveWorkflowApplier(WorkflowStore store, Func<string, string, CancellationToken, Task> publishToVoSpace)
    {
        _store = store;
        _publishToVoSpace = publishToVoSpace;
    }

    public string Kind => "save_workflow";

    public async Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
    {
        var p = ProposalPayload.Decode<SaveWorkflowPayload>(proposal);
        if (p.Location == "vospace")
            await _publishToVoSpace(WorkflowStore.Slugify(p.Name) + WorkflowFormat.FileExtension, p.Text, cancellationToken);
        else
            _store.SaveNew(p.Name, p.Text);
    }
}

public sealed class UpdateWorkflowApplier : IProposalApplier
{
    private readonly WorkflowStore _store;
    public UpdateWorkflowApplier(WorkflowStore store) => _store = store;
    public string Kind => "update_workflow";
    public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
    {
        var p = ProposalPayload.Decode<UpdateWorkflowPayload>(proposal);
        _store.UpdateText(p.Id, p.Text);
        return Task.CompletedTask;
    }
}

public sealed class SetWorkflowStepApplier : IProposalApplier
{
    private readonly WorkflowStore _store;
    public SetWorkflowStepApplier(WorkflowStore store) => _store = store;
    public string Kind => "set_workflow_step";
    public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
    {
        var p = ProposalPayload.Decode<SetWorkflowStepPayload>(proposal);
        _store.SetStepDone(p.Id, p.Index, p.Done);
        return Task.CompletedTask;
    }
}

public sealed class UseWorkflowApplier : IProposalApplier
{
    private readonly WorkflowStore _store;
    public UseWorkflowApplier(WorkflowStore store) => _store = store;
    public string Kind => "use_workflow";
    public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
    {
        var p = ProposalPayload.Decode<UseWorkflowPayload>(proposal);
        _store.UseWorkflow(p.Id, p.Name);
        return Task.CompletedTask;
    }
}

public sealed class DeleteWorkflowApplier : IProposalApplier
{
    private readonly WorkflowStore _store;
    public DeleteWorkflowApplier(WorkflowStore store) => _store = store;
    public string Kind => "delete_workflow";
    public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
    {
        var p = ProposalPayload.Decode<DeleteWorkflowPayload>(proposal);
        _store.Delete(p.Id);
        return Task.CompletedTask;
    }
}
