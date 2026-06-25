using System.Text;
using System.Text.Json;
using CanfarDesktop.Mcp.Tools.Proposals;

namespace CanfarDesktop.Mcp.Tools.Write;

// ─────────────────────────────────────────────────────────────────────────────
// VOSpace/ARC storage write tools + appliers. The tool validates + proposes; the
// applier decodes + invokes the injected IStorageService action. upload/create =
// SemanticWrite, delete = Destructive.
// ─────────────────────────────────────────────────────────────────────────────

public sealed record UploadTextPayload(string Path, string Content, string? ContentType);
public sealed record CreateFolderPayload(string Path, string Name);
public sealed record DeleteNodePayload(string Path);

/// <summary><c>upload_text_to_vospace</c> — propose writing a text blob (script/config) to a VOSpace path. SemanticWrite.</summary>
public sealed class UploadTextToVoSpaceTool : JsonWriteTool<UploadTextToVoSpaceTool.Args>
{
    public const int MaxContentBytes = 1024 * 1024; // 1 MB

    public override McpVerbClass VerbClass => McpVerbClass.SemanticWrite;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "upload_text_to_vospace",
        "Propose writing a text blob (e.g. a script or config) to a VOSpace/ARC file path (overwrites if it " +
        "exists; up to 1 MB). Queues for the user to apply.",
        """{"type":"object","properties":{"path":{"type":"string","description":"Destination VOSpace/ARC file path"},"content":{"type":"string"},"contentType":{"type":"string","description":"MIME type (default text/plain)"}},"required":["path","content"],"additionalProperties":false}""");

    protected override Task<ProposalPlan> PlanAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var path = (args.Path ?? string.Empty).Trim();
        if (path.Length == 0) throw new McpToolException(new InvalidArgument("path is required"));
        var content = args.Content ?? string.Empty;
        if (content.Length == 0) throw new McpToolException(new InvalidArgument("content is required"));
        if (Encoding.UTF8.GetByteCount(content) > MaxContentBytes)
            throw new McpToolException(new InvalidArgument($"content exceeds the {MaxContentBytes / 1024} KB limit"));

        var payload = new UploadTextPayload(path, content, string.IsNullOrWhiteSpace(args.ContentType) ? null : args.ContentType!.Trim());
        return Task.FromResult(ProposalPlan.Encoding("upload_text_to_vospace", $"Write {Encoding.UTF8.GetByteCount(content)} bytes to {path}", payload));
    }

    public sealed record Args
    {
        public string? Path { get; init; }
        public string? Content { get; init; }
        public string? ContentType { get; init; }
    }
}

/// <summary><c>create_vospace_folder</c> — propose creating a folder under a VOSpace path. SemanticWrite.</summary>
public sealed class CreateVoSpaceFolderTool : JsonWriteTool<CreateVoSpaceFolderTool.Args>
{
    public override McpVerbClass VerbClass => McpVerbClass.SemanticWrite;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "create_vospace_folder",
        "Propose creating a new folder with the given name under a VOSpace/ARC parent path. Queues for the user to apply.",
        """{"type":"object","properties":{"path":{"type":"string","description":"Parent VOSpace/ARC path"},"name":{"type":"string","description":"New folder name"}},"required":["path","name"],"additionalProperties":false}""");

    protected override Task<ProposalPlan> PlanAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var path = (args.Path ?? string.Empty).Trim();
        var name = (args.Name ?? string.Empty).Trim();
        if (path.Length == 0) throw new McpToolException(new InvalidArgument("path is required"));
        if (name.Length == 0) throw new McpToolException(new InvalidArgument("name is required"));

        return Task.FromResult(ProposalPlan.Encoding("create_vospace_folder", $"Create folder {name} in {path}", new CreateFolderPayload(path, name)));
    }

    public sealed record Args
    {
        public string? Path { get; init; }
        public string? Name { get; init; }
    }
}

/// <summary><c>delete_vospace_node</c> — propose deleting a VOSpace file or folder. Destructive.</summary>
public sealed class DeleteVoSpaceNodeTool : JsonWriteTool<DeleteVoSpaceNodeTool.Args>
{
    public override McpVerbClass VerbClass => McpVerbClass.Destructive;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "delete_vospace_node",
        "Propose deleting a VOSpace/ARC file or folder by its path. Queues for the user to apply (a destructive change).",
        """{"type":"object","properties":{"path":{"type":"string"}},"required":["path"],"additionalProperties":false}""");

    protected override Task<ProposalPlan> PlanAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var path = (args.Path ?? string.Empty).Trim();
        if (path.Length == 0) throw new McpToolException(new InvalidArgument("path is required"));
        return Task.FromResult(ProposalPlan.Encoding("delete_vospace_node", $"Delete {path}", new DeleteNodePayload(path)));
    }

    public sealed record Args { public string? Path { get; init; } }
}

public sealed class UploadTextToVoSpaceApplier : IProposalApplier
{
    private readonly Func<UploadTextPayload, Task> _upload;
    public UploadTextToVoSpaceApplier(Func<UploadTextPayload, Task> upload) => _upload = upload;
    public string Kind => "upload_text_to_vospace";
    public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
        => _upload(JsonSerializer.Deserialize<UploadTextPayload>(proposal.Payload, McpJson.Options)
                   ?? throw ProposalApplyException.BackendError("upload_text_to_vospace payload was empty"));
}

public sealed class CreateVoSpaceFolderApplier : IProposalApplier
{
    private readonly Func<CreateFolderPayload, Task> _create;
    public CreateVoSpaceFolderApplier(Func<CreateFolderPayload, Task> create) => _create = create;
    public string Kind => "create_vospace_folder";
    public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
        => _create(JsonSerializer.Deserialize<CreateFolderPayload>(proposal.Payload, McpJson.Options)
                   ?? throw ProposalApplyException.BackendError("create_vospace_folder payload was empty"));
}

public sealed class DeleteVoSpaceNodeApplier : IProposalApplier
{
    private readonly Func<DeleteNodePayload, Task> _delete;
    public DeleteVoSpaceNodeApplier(Func<DeleteNodePayload, Task> delete) => _delete = delete;
    public string Kind => "delete_vospace_node";
    public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
        => _delete(JsonSerializer.Deserialize<DeleteNodePayload>(proposal.Payload, McpJson.Options)
                   ?? throw ProposalApplyException.BackendError("delete_vospace_node payload was empty"));
}
