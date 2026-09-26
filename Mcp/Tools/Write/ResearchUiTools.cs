using CanfarDesktop.Models;
using CanfarDesktop.Services;

namespace CanfarDesktop.Mcp.Tools.Write;

// Research on the person's screen, and the clipboard: what a click on a record, a cutout's Original
// observation button and every Copy details do, for an agent. ViewState, like the Search page's own
// tools — they change what the person is looking at, or has copied, not what is stored.

/// <summary>What show_research_observation put on screen, and where.</summary>
/// <param name="Where">"research" when a record is shown there; "search" when Search was asked to find the observation.</param>
/// <param name="Id">The record shown in Research, by its id.</param>
/// <param name="ObservationId">The archive's observation id Search is finding, when it went there.</param>
public sealed record ResearchShown(bool Shown, string? Where, string? Id, string? ObservationId = null, string? Message = null)
{
    public static ResearchShown Refused(string message) => new(false, null, null, null, message);
}

/// <summary>
/// <c>show_research_observation</c> — one of Research's records on the person's screen, as a click on it
/// shows it; with <c>original</c>, a cutout's complete observation, as its Original observation button does.
/// </summary>
public sealed class ShowResearchObservationTool : JsonReadTool<ShowResearchObservationTool.Args, ResearchShown>
{
    private readonly Func<string, bool, Task<ResearchShown>> _show;

    public ShowResearchObservationTool(Func<string, bool, Task<ResearchShown>> show) => _show = show;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "show_research_observation",
        "Show one of Research's records on the person's screen: Research, with it selected and its detail " +
        "open, as a click on it does. id is its id from list_downloaded_observations, or its publisher id " +
        "(then the complete observation when Research keeps it beside cutouts of it), or the archive's " +
        "observation id. With original: true on a cutout, show the complete observation it was cut from " +
        "instead, as the cutout's Original observation button does: selected in Research when Research " +
        "keeps it, otherwise found in Search by its observation id, with the cutout's own row highlighted " +
        "(where says which). Changes nothing stored.",
        """{"type":"object","properties":{"id":{"type":"string"},"original":{"type":"boolean","description":"On a cutout: its complete observation, in Research or else in Search."}},"required":["id"],"additionalProperties":false}""");

    protected override Task<ResearchShown> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var id = (args.Id ?? string.Empty).Trim();
        if (id.Length == 0) throw new McpToolException(new InvalidArgument("id is required"));
        return _show(id, args.Original == true);
    }

    public sealed record Args
    {
        public string? Id { get; init; }
        public bool? Original { get; init; }
    }
}

/// <summary>What copy_to_clipboard put there.</summary>
public sealed record ClipboardCopied(bool Copied, int Characters, string? Message = null);

/// <summary>
/// <c>copy_to_clipboard</c> — text on the person's clipboard, as every Copy in the app puts it there and
/// says so in the status bar; or, by a Research record's id, its details — the same text Copy details
/// copies from Research, the observation view and a search result.
/// </summary>
public sealed class CopyToClipboardTool : JsonReadTool<CopyToClipboardTool.Args, ClipboardCopied>
{
    private readonly Func<string, DownloadedObservation?> _find;
    private readonly Func<string, string?, Task<bool>> _copy;

    /// <param name="find">A Research record by any id a person or an agent is likely to hold.</param>
    /// <param name="copy">Put the text on the clipboard, saying what it is; false when the clipboard refused it.</param>
    public CopyToClipboardTool(Func<string, DownloadedObservation?> find, Func<string, string?, Task<bool>> copy)
        => (_find, _copy) = (find, copy);

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "copy_to_clipboard",
        "Put text on the person's clipboard, as the app's Copy buttons and menus do — the status bar says " +
        "what was copied. Give text, or observationId (a Research record's id, its publisher id or the " +
        "archive's observation id) to copy that observation's details: ID, collection, publisher ID, " +
        "target, position (sexagesimal, with degrees), instrument, filter, date, calibration level, " +
        "proposal, release, and for a cutout its region — the same text Copy details copies. It replaces " +
        "whatever the person had copied, so copy only when asked to.",
        """{"type":"object","properties":{"text":{"type":"string"},"observationId":{"type":"string"}},"additionalProperties":false}""");

    protected override async Task<ClipboardCopied> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var (text, observation) = (args.Text, (args.ObservationId ?? string.Empty).Trim());
        if ((text is null) == (observation.Length == 0))
            throw new McpToolException(new InvalidArgument("give text, or observationId — one of the two"));

        string? what = null;
        if (observation.Length > 0)
        {
            var record = _find(observation)
                ?? throw new McpToolException(new InvalidArgument(
                    $"'{observation}' is not in Research — list_downloaded_observations shows what is"));
            text = ObservationSummary.Text(record);
            what = "the observation's details";
        }
        if (string.IsNullOrEmpty(text)) throw new McpToolException(new InvalidArgument("there is nothing to copy"));

        return await _copy(text, what)
            ? new ClipboardCopied(true, text.Length)
            : new ClipboardCopied(false, 0, "the clipboard is in use by another program; try again");
    }

    public sealed record Args
    {
        public string? Text { get; init; }
        public string? ObservationId { get; init; }
    }
}
