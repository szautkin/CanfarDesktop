using System.Globalization;
using System.Text.Json.Serialization;
using CanfarDesktop.Mcp.Tools.Proposals;

namespace CanfarDesktop.Mcp.Tools.Read;

/// <summary>
/// <c>list_events</c> — token-cursor poll of the proposal-lifecycle event log. Agents pass back the
/// highest token they've seen in <c>since_token</c>; the response carries new entries plus a
/// <c>nextToken</c> to use on the following poll. Verb class ProposalLifecycle (manages the agent's
/// own write lifecycle, like the other proposal tools). 1-to-1 with the macOS ListEventsTool.
/// </summary>
public sealed class ListEventsTool : JsonReadTool<ListEventsTool.Args, ListEventsTool.Output>
{
    private readonly AgentEventLog _log;

    public ListEventsTool(AgentEventLog log) => _log = log;

    public override McpVerbClass VerbClass => McpVerbClass.ProposalLifecycle;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "list_events",
        "Poll the agent event log. Pass `since_token` to read only events newer than that token. " +
        "Response includes nextToken to use on the following poll. If `expired` is true, your token is " +
        "older than the buffer; re-baseline with an empty since_token.",
        """{"type":"object","properties":{"since_token":{"type":"string","description":"Token from a previous response. Empty/absent reads from the start of the retained buffer."}},"additionalProperties":false}""");

    protected override Task<Output> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        // The token is a string on the wire (dodges JSON number-precision concerns, per macOS).
        // Absent/empty/malformed parses to 0 = read from the start of the retained buffer.
        _ = ulong.TryParse(args.SinceToken, NumberStyles.None, CultureInfo.InvariantCulture, out var since);

        var (entries, expired, nextToken) = _log.EntriesSince(since);
        var items = entries
            .Select(e => new Item(
                e.Token.ToString(CultureInfo.InvariantCulture),
                e.OccurredAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                e.Kind,
                e.ProposalId.ToString(),
                e.ProposalKind,
                e.OriginKind))
            .ToList();

        return Task.FromResult(new Output(items, nextToken.ToString(CultureInfo.InvariantCulture), expired));
    }

    public sealed record Args
    {
        [JsonPropertyName("since_token")] public string? SinceToken { get; init; }
    }

    /// <summary><c>kind</c> is proposalArrived | proposalApplied | proposalRejected | proposalWithdrawn;
    /// <c>originKind</c> (user/external) is only set for proposalArrived.</summary>
    public sealed record Item(
        string Token, string OccurredAtISO, string Kind, string ProposalID, string ProposalKind, string? OriginKind);

    public sealed record Output(IReadOnlyList<Item> Events, string NextToken, bool Expired);
}
