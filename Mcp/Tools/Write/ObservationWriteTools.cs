using System.Text.Json;
using CanfarDesktop.Mcp.Tools.Proposals;

namespace CanfarDesktop.Mcp.Tools.Write;

// ─────────────────────────────────────────────────────────────────────────────
// Downloaded-observation write tools + appliers. download_observation pulls an
// observation's FITS into the Research module; delete_downloaded_observation
// removes it. The tool validates + proposes; the applier decodes + invokes the
// injected host action (download orchestration / store removal lives host-side).
// ─────────────────────────────────────────────────────────────────────────────

public sealed record DownloadObservationPayload(string PublisherId, int? ArtifactIndex = null);
public sealed record DeleteDownloadedObservationPayload(string Id);

/// <summary><c>download_observation</c> — propose downloading an observation's FITS into Research. SemanticWrite.</summary>
public sealed class DownloadObservationTool : JsonWriteTool<DownloadObservationTool.Args>
{
    public override McpVerbClass VerbClass => McpVerbClass.SemanticWrite;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "download_observation",
        "Propose downloading a CADC observation's FITS file into the Research module by its publisher id " +
        "(from search_observations). Optional `artifactIndex` (from list_observation_artifacts) picks a " +
        "SPECIFIC product — e.g. the science cube, a moment map, or the integrated spectrum — instead of " +
        "the default first/primary artifact. Proprietary/embargoed collections require the user to be " +
        "signed in to CADC. Queues for the user to apply; after it applies it appears in " +
        "list_downloaded_observations.",
        """{"type":"object","properties":{"publisherId":{"type":"string"},"artifactIndex":{"type":"integer","minimum":0}},"required":["publisherId"],"additionalProperties":false}""");

    protected override Task<ProposalPlan> PlanAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var pid = (args.PublisherId ?? string.Empty).Trim();
        if (pid.Length == 0) throw new McpToolException(new InvalidArgument("publisherId is required"));
        var summary = args.ArtifactIndex is int ai ? $"Download observation {pid} (artifact #{ai})" : $"Download observation {pid}";
        return Task.FromResult(ProposalPlan.Encoding("download_observation", summary, new DownloadObservationPayload(pid, args.ArtifactIndex)));
    }

    public sealed record Args { public string? PublisherId { get; init; } public int? ArtifactIndex { get; init; } }
}

/// <summary><c>delete_downloaded_observation</c> — propose removing a downloaded observation. Destructive.</summary>
public sealed class DeleteDownloadedObservationTool : JsonWriteTool<DeleteDownloadedObservationTool.Args>
{
    public override McpVerbClass VerbClass => McpVerbClass.Destructive;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "delete_downloaded_observation",
        "Propose removing a downloaded observation from Research by its local id (from " +
        "list_downloaded_observations) or its publisher id. Queues for the user to apply (a destructive change).",
        """{"type":"object","properties":{"id":{"type":"string"}},"required":["id"],"additionalProperties":false}""");

    protected override Task<ProposalPlan> PlanAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var id = (args.Id ?? string.Empty).Trim();
        if (id.Length == 0) throw new McpToolException(new InvalidArgument("id is required"));
        return Task.FromResult(ProposalPlan.Encoding("delete_downloaded_observation", $"Delete downloaded observation {id}", new DeleteDownloadedObservationPayload(id)));
    }

    public sealed record Args { public string? Id { get; init; } }
}

public sealed class DownloadObservationApplier : IProposalApplier
{
    private readonly Func<DownloadObservationPayload, Task> _download;
    public DownloadObservationApplier(Func<DownloadObservationPayload, Task> download) => _download = download;
    public string Kind => "download_observation";
    public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
        => _download(ProposalPayload.Decode<DownloadObservationPayload>(proposal));
}

public sealed class DeleteDownloadedObservationApplier : IProposalApplier
{
    private readonly Func<DeleteDownloadedObservationPayload, Task> _delete;
    public DeleteDownloadedObservationApplier(Func<DeleteDownloadedObservationPayload, Task> delete) => _delete = delete;
    public string Kind => "delete_downloaded_observation";
    public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
        => _delete(ProposalPayload.Decode<DeleteDownloadedObservationPayload>(proposal));
}
