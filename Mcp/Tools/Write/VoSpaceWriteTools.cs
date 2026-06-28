using System.Net;
using System.Net.Http;
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
public sealed record UploadFilePayload(string LocalPath, string VospacePath, string? ContentType);
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

/// <summary><c>upload_file_to_vospace</c> — propose uploading a local file (binary OK) to a VOSpace path. SemanticWrite.</summary>
public sealed class UploadFileToVoSpaceTool : JsonWriteTool<UploadFileToVoSpaceTool.Args>
{
    public override McpVerbClass VerbClass => McpVerbClass.SemanticWrite;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "upload_file_to_vospace",
        "Propose uploading a LOCAL file (any type, including binary) to a VOSpace/ARC destination path " +
        "(overwrites if it exists). Use this for real files; upload_text_to_vospace is only for small text " +
        "blobs. Queues for the user to apply.",
        """{"type":"object","properties":{"localPath":{"type":"string","description":"Local filesystem path of the file to upload"},"vospacePath":{"type":"string","description":"Destination VOSpace/ARC file path"},"contentType":{"type":"string","description":"MIME type (optional)"}},"required":["localPath","vospacePath"],"additionalProperties":false}""");

    protected override Task<ProposalPlan> PlanAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var local = (args.LocalPath ?? string.Empty).Trim();
        var remote = (args.VospacePath ?? string.Empty).Trim();
        if (local.Length == 0) throw new McpToolException(new InvalidArgument("localPath is required"));
        if (remote.Length == 0) throw new McpToolException(new InvalidArgument("vospacePath is required"));
        var full = ToolPaths.RequireRootedFullPath(local, "localPath");

        var payload = new UploadFilePayload(full, remote, string.IsNullOrWhiteSpace(args.ContentType) ? null : args.ContentType!.Trim());
        return Task.FromResult(ProposalPlan.Encoding("upload_file_to_vospace", $"Upload {Path.GetFileName(full)} → {remote}", payload));
    }

    public sealed record Args
    {
        public string? LocalPath { get; init; }
        public string? VospacePath { get; init; }
        public string? ContentType { get; init; }
    }
}

/// <summary>
/// <c>download_vospace_file</c> — stream a VOSpace/ARC file to a local path (handles large/binary files
/// that read_vospace_file can't return inline). Verb class ViewState: live-applied, path-validated.
/// </summary>
public sealed class DownloadVoSpaceFileTool : JsonReadTool<DownloadVoSpaceFileTool.Args, DownloadVoSpaceFileTool.Output>
{
    private const int DownloadTimeoutSeconds = 120;

    private readonly Func<string, CancellationToken, Task<Stream>> _download;
    public DownloadVoSpaceFileTool(Func<string, CancellationToken, Task<Stream>> download) => _download = download;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "download_vospace_file",
        "Stream a VOSpace/ARC file to a LOCAL filesystem path (handles large/binary files that " +
        "read_vospace_file can't return inline). The local path must be a full path in an existing folder; an " +
        "existing file is overwritten. Returns the bytes written.",
        """{"type":"object","properties":{"path":{"type":"string","description":"VOSpace/ARC file path to download"},"localPath":{"type":"string","description":"Destination local filesystem path (full path)"}},"required":["path","localPath"],"additionalProperties":false}""");

    protected override async Task<Output> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var remote = (args.Path ?? string.Empty).Trim();
        var local = (args.LocalPath ?? string.Empty).Trim();
        if (remote.Length == 0) throw new McpToolException(new InvalidArgument("path is required"));
        if (local.Length == 0) throw new McpToolException(new InvalidArgument("localPath is required"));
        var full = ToolPaths.RequireRootedFullPath(local, "localPath");

        // Bound the whole transfer so a stalled VOSpace stream can't run to the tool ceiling; honour the caller's token.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(DownloadTimeoutSeconds));
        var token = cts.Token;

        Stream stream;
        try { stream = await _download(remote, token); }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new McpToolException(new AuthRequired());
        }

        long total = 0;
        try
        {
            await using (stream)
            await using (var fs = new FileStream(full, FileMode.Create, FileAccess.Write))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(buffer, token)) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, read), token);
                    total += read;
                }
            }
        }
        catch (DirectoryNotFoundException)
        {
            throw new McpToolException(new InvalidArgument($"the destination folder does not exist: {Path.GetDirectoryName(full)}"));
        }

        return new Output(remote, full, total);
    }

    public sealed record Args { public string? Path { get; init; } public string? LocalPath { get; init; } }
    public sealed record Output(string Path, string LocalPath, long BytesWritten);
}

public sealed class UploadTextToVoSpaceApplier : IProposalApplier
{
    private readonly Func<UploadTextPayload, Task> _upload;
    public UploadTextToVoSpaceApplier(Func<UploadTextPayload, Task> upload) => _upload = upload;
    public string Kind => "upload_text_to_vospace";
    public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
        => _upload(ProposalPayload.Decode<UploadTextPayload>(proposal));
}

public sealed class UploadFileToVoSpaceApplier : IProposalApplier
{
    private readonly Func<UploadFilePayload, Task> _upload;
    public UploadFileToVoSpaceApplier(Func<UploadFilePayload, Task> upload) => _upload = upload;
    public string Kind => "upload_file_to_vospace";
    public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
        => _upload(ProposalPayload.Decode<UploadFilePayload>(proposal));
}

public sealed class CreateVoSpaceFolderApplier : IProposalApplier
{
    private readonly Func<CreateFolderPayload, Task> _create;
    public CreateVoSpaceFolderApplier(Func<CreateFolderPayload, Task> create) => _create = create;
    public string Kind => "create_vospace_folder";
    public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
        => _create(ProposalPayload.Decode<CreateFolderPayload>(proposal));
}

public sealed class DeleteVoSpaceNodeApplier : IProposalApplier
{
    private readonly Func<DeleteNodePayload, Task> _delete;
    public DeleteVoSpaceNodeApplier(Func<DeleteNodePayload, Task> delete) => _delete = delete;
    public string Kind => "delete_vospace_node";
    public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
        => _delete(ProposalPayload.Decode<DeleteNodePayload>(proposal));
}
