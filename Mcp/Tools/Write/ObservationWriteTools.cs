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
public sealed record DownloadObservationsBulkPayload(IReadOnlyList<DownloadObservationPayload> Items);
public sealed record DeleteDownloadedObservationPayload(string Id);
public sealed record ClearResearchArchivePayload();

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

/// <summary>
/// <c>download_observations_bulk</c> — propose downloading up to 50 observations as one proposal
/// envelope (one user click). Each item takes the same publisherId/artifactIndex shape as the
/// singular <c>download_observation</c>; the applier runs the same per-item download in sequence.
/// SemanticWrite.
/// </summary>
public sealed class DownloadObservationsBulkTool : JsonWriteTool<DownloadObservationsBulkTool.Args>
{
    public const int MaxBatchSize = 50;

    public override McpVerbClass VerbClass => McpVerbClass.SemanticWrite;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "download_observations_bulk",
        "Download up to 50 observations as one proposal envelope. The applier downloads each in " +
        "sequence; first failure aborts the rest. Note: total in-flight time can exceed the MCP request " +
        "timeout for large batches — prefer staging in groups of ~10 if the items are big FITS files.",
        """{"type":"object","required":["items"],"properties":{"items":{"type":"array","minItems":1,"maxItems":50,"items":{"type":"object","properties":{"publisherId":{"type":"string"},"artifactIndex":{"type":"integer","minimum":0}},"required":["publisherId"],"additionalProperties":false}}},"additionalProperties":false}""");

    protected override Task<ProposalPlan> PlanAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var items = args.Items ?? Array.Empty<Item>();
        if (items.Count == 0) throw new McpToolException(new InvalidArgument("items is empty"));
        if (items.Count > MaxBatchSize)
            throw new McpToolException(new InvalidArgument($"max {MaxBatchSize} items per bulk download"));

        var payloads = new List<DownloadObservationPayload>(items.Count);
        foreach (var item in items)
        {
            var pid = (item.PublisherId ?? string.Empty).Trim();
            if (pid.Length == 0) throw new McpToolException(new InvalidArgument("every item requires a publisherId"));
            payloads.Add(new DownloadObservationPayload(pid, item.ArtifactIndex));
        }

        var summary = $"Download {payloads.Count} observation{(payloads.Count == 1 ? "" : "s")}";
        return Task.FromResult(ProposalPlan.Encoding(
            "download_observations_bulk", summary, new DownloadObservationsBulkPayload(payloads)));
    }

    public sealed record Item { public string? PublisherId { get; init; } public int? ArtifactIndex { get; init; } }
    public sealed record Args { public IReadOnlyList<Item>? Items { get; init; } }
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

/// <summary>
/// <c>clear_research_archive</c> — propose removing EVERY downloaded observation from Research.
/// Unlike macOS (metadata-only clear), the Windows applier also deletes the local files
/// (best-effort) and each observation's notes. Destructive.
/// </summary>
public sealed class ClearResearchArchiveTool : JsonWriteTool<EmptyArgs>
{
    public override McpVerbClass VerbClass => McpVerbClass.Destructive;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "clear_research_archive",
        "Remove ALL downloaded-observation records from Research — their metadata, their notes, and " +
        "their local files (file deletion is best-effort). Queues for the user to apply (a destructive change).",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override Task<ProposalPlan> PlanAsync(EmptyArgs args, McpToolContext context, CancellationToken ct)
        => Task.FromResult(ProposalPlan.Encoding(
            "clear_research_archive", "Clear ALL research archive records", new ClearResearchArchivePayload()));
}

public sealed class DownloadObservationApplier : IProposalApplier
{
    private readonly Func<DownloadObservationPayload, Models.AgentAttribution?, Task> _download;

    public DownloadObservationApplier(Func<DownloadObservationPayload, Task> download) : this((p, _) => download(p)) { }

    /// <summary>Attribution-aware overload: the stamp the downloaded record should carry (null for user origin).</summary>
    public DownloadObservationApplier(Func<DownloadObservationPayload, Models.AgentAttribution?, Task> download) => _download = download;

    public string Kind => "download_observation";

    public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
        => _download(ProposalPayload.Decode<DownloadObservationPayload>(proposal), Agents.AgentAttributionStamp.ForProposal(proposal));
}

/// <summary>Applies <c>download_observations_bulk</c>: the same attribution-stamped per-item download
/// the singular applier runs, in sequence — the first failure aborts the rest (the tool's advertised
/// contract, matching macOS).</summary>
public sealed class DownloadObservationsBulkApplier : IProposalApplier
{
    private readonly Func<DownloadObservationPayload, Models.AgentAttribution?, Task> _download;

    public DownloadObservationsBulkApplier(Func<DownloadObservationPayload, Models.AgentAttribution?, Task> download)
        => _download = download;

    public string Kind => "download_observations_bulk";

    public async Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
    {
        var payload = ProposalPayload.Decode<DownloadObservationsBulkPayload>(proposal);
        var attribution = Agents.AgentAttributionStamp.ForProposal(proposal);
        foreach (var item in payload.Items)
        {
            // Honor the host's apply-timeout cancellation: without this a timed-out bulk keeps
            // downloading (and holding the apply gate) long after the agent was told it failed.
            cancellationToken.ThrowIfCancellationRequested();
            await _download(item, attribution);
        }
    }
}

public sealed class DeleteDownloadedObservationApplier : IProposalApplier
{
    private readonly Func<DeleteDownloadedObservationPayload, Task> _delete;
    public DeleteDownloadedObservationApplier(Func<DeleteDownloadedObservationPayload, Task> delete) => _delete = delete;
    public string Kind => "delete_downloaded_observation";
    public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
        => _delete(ProposalPayload.Decode<DeleteDownloadedObservationPayload>(proposal));
}

/// <summary>
/// Applies <c>clear_research_archive</c>: for every downloaded observation, deletes its local file
/// (best-effort — a locked/missing file never aborts the clear), removes the record, and deletes its
/// notes. Delegates keep it pure/testable; the catalog binds them to ObservationStore /
/// ObservationNoteStore / File.Delete.
/// </summary>
public sealed class ClearResearchArchiveApplier : IProposalApplier
{
    private readonly Func<IReadOnlyList<Models.DownloadedObservation>> _observations;
    private readonly Action<Models.DownloadedObservation> _remove;
    private readonly Action<string> _deleteNote;
    private readonly Action<string> _deleteFile;

    public ClearResearchArchiveApplier(
        Func<IReadOnlyList<Models.DownloadedObservation>> observations,
        Action<Models.DownloadedObservation> remove,
        Action<string> deleteNote,
        Action<string> deleteFile)
    {
        _observations = observations;
        _remove = remove;
        _deleteNote = deleteNote;
        _deleteFile = deleteFile;
    }

    public string Kind => "clear_research_archive";

    public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default)
    {
        foreach (var observation in _observations())
        {
            if (!string.IsNullOrWhiteSpace(observation.LocalPath))
            {
                try { _deleteFile(observation.LocalPath); }
                catch { /* best-effort: keep clearing the archive even if a file is locked/missing */ }
            }
            _remove(observation);
            _deleteNote(observation.PublisherID);
        }
        return Task.CompletedTask;
    }
}
