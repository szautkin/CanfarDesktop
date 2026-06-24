using CanfarDesktop.Models;

namespace CanfarDesktop.Mcp.Tools.Read;

/// <summary>Compact view of a downloaded observation for MCP output (no live filesystem probing).</summary>
public sealed record ObservationSummary(
    string Id, string PublisherId, string Collection, string ObservationId, string TargetName,
    string Instrument, string Filter, string Ra, string Dec, string StartDate, string CalLevel,
    string Filename, long? FileSizeBytes, DateTime DownloadedAt)
{
    public static ObservationSummary From(DownloadedObservation o) => new(
        o.Id, o.PublisherID, o.Collection, o.ObservationID, o.TargetName, o.Instrument, o.Filter,
        o.RA, o.Dec, o.StartDate, o.CalLevel,
        string.IsNullOrEmpty(o.LocalPath) ? "" : Path.GetFileName(o.LocalPath), o.FileSize, o.DownloadedAt);
}

/// <summary><c>list_downloaded_observations</c> — the observations in the user's Research module.</summary>
public sealed class ListDownloadedObservationsTool : JsonReadTool<EmptyArgs, ListDownloadedObservationsTool.Output>
{
    private readonly Func<IReadOnlyList<DownloadedObservation>> _all;

    public ListDownloadedObservationsTool(Func<IReadOnlyList<DownloadedObservation>> all) => _all = all;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "list_downloaded_observations",
        "List the observations the user has downloaded to the Research module (id, target, collection, instrument).",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override Task<Output> HandleAsync(EmptyArgs args, McpToolContext context, CancellationToken ct)
    {
        var items = _all().Select(ObservationSummary.From).ToList();
        return Task.FromResult(new Output(items.Count, items));
    }

    public sealed record Output(int Count, IReadOnlyList<ObservationSummary> Observations);
}

/// <summary><c>get_downloaded_observation</c> — one observation by its local id.</summary>
public sealed class GetDownloadedObservationTool : JsonReadTool<GetDownloadedObservationTool.Args, ObservationSummary>
{
    private readonly Func<IReadOnlyList<DownloadedObservation>> _all;

    public GetDownloadedObservationTool(Func<IReadOnlyList<DownloadedObservation>> all) => _all = all;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_downloaded_observation",
        "Get one downloaded observation by its local id (from list_downloaded_observations).",
        """{"type":"object","properties":{"id":{"type":"string","description":"Local observation id"}},"required":["id"],"additionalProperties":false}""");

    protected override Task<ObservationSummary> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args.Id))
            throw new McpToolException(new InvalidArgument("id is required"));
        var match = _all().FirstOrDefault(o => o.Id == args.Id);
        if (match is null)
            throw new McpToolException(new UnknownTarget(args.Id));
        return Task.FromResult(ObservationSummary.From(match));
    }

    public sealed record Args
    {
        public string Id { get; init; } = string.Empty;
    }
}

/// <summary><c>get_observation_notes</c> — astronomer notes, all or for one publisher id.</summary>
public sealed class GetObservationNotesTool : JsonReadTool<GetObservationNotesTool.Args, GetObservationNotesTool.Output>
{
    private readonly Func<IReadOnlyList<ObservationNote>> _all;

    public GetObservationNotesTool(Func<IReadOnlyList<ObservationNote>> all) => _all = all;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_observation_notes",
        "Get the user's research notes (note text, 0-5 rating, tags) — all of them, or for one publisher id.",
        """{"type":"object","properties":{"publisherId":{"type":"string","description":"Optional: restrict to one observation's publisher id"}},"additionalProperties":false}""");

    protected override Task<Output> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var notes = _all().AsEnumerable();
        if (!string.IsNullOrWhiteSpace(args.PublisherId))
            notes = notes.Where(n => n.PublisherID == args.PublisherId);

        var items = notes.Select(n => new NoteView(n.PublisherID, n.Note, n.Rating, n.Tags, n.UpdatedUtc)).ToList();
        return Task.FromResult(new Output(items.Count, items));
    }

    public sealed record Args
    {
        public string? PublisherId { get; init; }
    }

    public sealed record NoteView(string PublisherId, string Note, int Rating, IReadOnlyList<string> Tags, DateTimeOffset UpdatedUtc);
    public sealed record Output(int Count, IReadOnlyList<NoteView> Notes);
}
