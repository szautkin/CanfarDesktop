namespace CanfarDesktop.Mcp.Tools.Write;

/// <summary>One thing on screen that can be pointed at.</summary>
/// <param name="Target">The name to pass to <c>point_at_ui</c>.</param>
/// <param name="Kind">What sort of control it is — a Button, a TextBox, a ToggleSwitch.</param>
/// <param name="Label">What it says to the person, when it says anything.</param>
public sealed record UiTarget(string Target, string Kind, string? Label);

/// <summary>
/// What is on screen to point at, and which closed sections were left out of it.
/// </summary>
/// <param name="CollapsedSections">
/// Sections that are folded shut, whose controls are not in <paramref name="Targets"/> unless the
/// listing was asked to include them. Named so a listing never silently omits half a page.
/// </param>
/// <param name="Dialog">
/// The title of the dialog these controls are in, when one is open — Settings, say. The window behind
/// an open dialog cannot be clicked, so its controls are not listed.
/// </param>
public sealed record UiTargetListing(
    IReadOnlyList<UiTarget> Targets, IReadOnlyList<UiTarget> CollapsedSections, string? Dialog = null);

/// <summary>What <c>point_at_ui</c> was asked for.</summary>
/// <param name="UntilClosed">
/// No countdown: the hint stays until the person closes it or the page changes. For the slow guide
/// somebody asks for, where the tour waits on them rather than on a clock.
/// </param>
public sealed record UiPointRequest(
    string Target, string? Title, string Message, double? Seconds, bool UntilClosed = false);

/// <summary>
/// What came of it.
/// </summary>
/// <param name="Candidates">
/// What IS on screen, whenever the name did not land on exactly one thing. A refusal that does not
/// say what would have worked leaves the caller guessing a second time.
/// </param>
public sealed record UiPointOutcome(
    bool Pointed, string? Target, string? Message, IReadOnlyList<UiTarget>? Candidates = null);

/// <summary>
/// <c>point_at_ui</c> — show the person where a control is.
///
/// <para>Every other tool here lets an agent DO something in the app. This is the one that lets it
/// show somebody where a thing is, which is the one thing it could not do at all: the answer to
/// "where do I change the stretch?" used to be a paragraph describing a route through panels that the
/// person then had to follow by eye, against a screen the agent could see and they were looking at.
/// Now the app points, with a small tip whose tail touches the control.</para>
///
/// <para>It shows; it does not press. Pointing at Delete is safe, and the person decides. That is the
/// point of having it as well as the tools that act: some things should be done TO the app by an
/// agent, and some things a person should be walked to and left to choose.</para>
///
/// <para>Verb class ViewState: it puts a transient tip on screen and changes no data.</para>
/// </summary>
public sealed class PointAtUiTool : JsonReadTool<PointAtUiTool.Args, UiPointOutcome>
{
    private readonly Func<UiPointRequest, Task<UiPointOutcome>> _point;

    public PointAtUiTool(Func<UiPointRequest, Task<UiPointOutcome>> point) => _point = point;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "point_at_ui",
        "Show the person where a control is: a small popover with a tail touching it, plus a sentence " +
        "of your own. Use it when someone asks where something is, or when you want them to do the " +
        "next step themselves — it points, it never presses, so pointing at a destructive control is " +
        "safe. 'target' is a control's name or the words on it, as listed by list_ui_targets; only " +
        "what is on this page can be pointed at, so navigate first if you need to. A control inside a " +
        "collapsed section is still reachable — the section is opened for you before the hint goes up. " +
        "If the name " +
        "matches nothing, or two things equally, nothing is shown and the reply lists what is there. " +
        "Call it several times to put several hints up at once — one per control, so pointing at the " +
        "same one twice replaces rather than stacks. Each has its own close button and fades on its " +
        "own after 'seconds' — hovering pauses it, moving off starts it again — and they all go the " +
        "moment the app changes page, so hints never outlive the screen they describe. When the last " +
        "one goes, however it went, list_events carries a hintsDismissed event: poll for it to pace a " +
        "tour. Pass untilClosed only when the person has asked for a slow guide — the hint then waits " +
        "for its close button instead of fading. Live-applied.",
        """
        {"type":"object","properties":{
          "target":{"type":"string","description":"The control's name, or the words on it. See list_ui_targets."},
          "message":{"type":"string","description":"One sentence telling them what this control does or why they want it."},
          "title":{"type":"string","description":"Optional short heading above the message."},
          "seconds":{"type":"number","description":"How long it stays up. Default 8, clamped to 2-60."},
          "untilClosed":{"type":"boolean","description":"No countdown: it stays until the person closes it or the page changes. For a slow guide they asked for. Default false."}
        },"required":["target","message"],"additionalProperties":false}
        """);

    protected override async Task<UiPointOutcome> HandleAsync(
        Args args, McpToolContext context, CancellationToken ct)
    {
        var target = (args.Target ?? string.Empty).Trim();
        if (target.Length == 0) throw new McpToolException(new InvalidArgument("target is required"));

        var message = (args.Message ?? string.Empty).Trim();
        if (message.Length == 0)
            throw new McpToolException(new InvalidArgument(
                "message is required — a tip pointing at a control without saying why is a shape on the screen"));

        return await _point(new UiPointRequest(
            target, Args.Clean(args.Title), message, args.Seconds, args.UntilClosed == true));
    }

    public sealed record Args
    {
        public string? Target { get; init; }
        public string? Title { get; init; }
        public string? Message { get; init; }
        public double? Seconds { get; init; }
        public bool? UntilClosed { get; init; }

        internal static string? Clean(string? text)
            => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}

/// <summary>
/// <c>list_ui_targets</c> — what can be pointed at on the screen as it is now.
///
/// <para>The vocabulary for <c>point_at_ui</c>, and it changes with the screen: a control on a page
/// that is not showing is not somewhere a person can be sent. Read this rather than guessing a name,
/// or navigate first and then read it.</para>
/// </summary>
public sealed class ListUiTargetsTool : JsonReadTool<ListUiTargetsTool.Args, ListUiTargetsTool.Result>
{
    private readonly Func<string?, bool, Task<UiTargetListing>> _list;

    public ListUiTargetsTool(Func<string?, bool, Task<UiTargetListing>> list) => _list = list;

    public override McpVerbClass VerbClass => McpVerbClass.Read;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "list_ui_targets",
        "The controls on screen right now that point_at_ui can point at, each with its name, what " +
        "sort of control it is, and the words it shows. Optionally filtered by 'contains'. This " +
        "changes as the app navigates — a control on a page that is not showing is not in the list, " +
        "because it is not somewhere a person can be sent. Sections that are folded shut are named " +
        "in collapsedSections and their controls left out; pass includeCollapsed to list those too " +
        "(the sections open for a moment to be read and close again). While a dialog is open — Settings, " +
        "opened with open_settings, say — the list is that dialog's controls and 'dialog' names it: the " +
        "window behind a dialog cannot be clicked, so it is not where to point. Read-only.",
        """
        {"type":"object","properties":{
          "contains":{"type":"string","description":"Only targets whose name or words contain this."},
          "includeCollapsed":{"type":"boolean","description":"Also list controls inside closed sections. Default false."}
        },"additionalProperties":false}
        """);

    protected override async Task<Result> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var listing = await _list(
            string.IsNullOrWhiteSpace(args.Contains) ? null : args.Contains!.Trim(),
            args.IncludeCollapsed == true);
        return new Result(listing.Targets.Count, listing.Targets, listing.CollapsedSections, listing.Dialog);
    }

    public sealed record Args
    {
        public string? Contains { get; init; }
        public bool? IncludeCollapsed { get; init; }
    }

    public sealed record Result(
        int Count, IReadOnlyList<UiTarget> Targets, IReadOnlyList<UiTarget> CollapsedSections, string? Dialog = null);
}
