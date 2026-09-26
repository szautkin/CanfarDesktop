using CanfarDesktop.Helpers;

namespace CanfarDesktop.Mcp.Tools.Write;

/// <summary>Where the Settings dialog stands after an agent opened or closed it.</summary>
/// <param name="Open">Whether Settings is open now.</param>
/// <param name="Section">The section showing, while it is open.</param>
/// <param name="Message">Why it is not as asked, when it is not.</param>
public sealed record SettingsShown(bool Open, string? Section, string? Message = null);

/// <summary>One section of Settings, as open_settings lists them.</summary>
public sealed record SettingsSectionView(string Section, string Title, string Holds);

/// <summary>
/// <c>open_settings</c> — open the Settings dialog at a section, so the person can see it.
///
/// <para>For guiding, not for setting: an agent shows where something is set and points at it with
/// point_at_ui, and the person sets it. Some settings only they should ever change — the MCP server
/// and auto-apply, the service endpoints, the compute image and the sign-ins — and walking them there
/// is the whole of what an agent can do about those.</para>
///
/// <para>Verb class ViewState: it opens a dialog and changes no data.</para>
/// </summary>
public sealed class OpenSettingsTool : JsonReadTool<OpenSettingsTool.Args, OpenSettingsTool.Result>
{
    private readonly Func<string?, Task<SettingsShown>> _open;

    public OpenSettingsTool(Func<string?, Task<SettingsShown>> open) => _open = open;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "open_settings",
        "Open the Settings dialog at a section so the person can see it — to show them where something " +
        "is set, or to walk them through setting it themselves. It changes nothing: settings are the " +
        "person's to set, and some — the MCP server and auto-apply, the service endpoints, the compute " +
        "image, the sign-ins — only they should change. Once it is open, list_ui_targets and point_at_ui " +
        "work inside it (the window behind a dialog cannot be clicked, so that is not where to point). " +
        "If Settings is already open this switches its section; switching or closing takes any hints " +
        "down with it. The reply lists every section and what it holds. Close it with close_settings, " +
        "or leave that to the person. Live-applied.",
        $$"""
        {"type":"object","properties":{
          "section":{"type":"string","enum":[{{string.Join(",", SettingsSections.All.Select(s => $"\"{s.Id}\""))}}],
                     "description":"Which section to show. Default: the one showing, or general."}
        },"additionalProperties":false}
        """);

    protected override async Task<Result> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        string? section = null;
        if (!string.IsNullOrWhiteSpace(args.Section))
        {
            section = SettingsSections.Find(args.Section)?.Id
                ?? throw new McpToolException(new InvalidArgument(
                    $"no Settings section \"{args.Section.Trim()}\" — the sections are " +
                    string.Join(", ", SettingsSections.All.Select(s => s.Id))));
        }

        var shown = await _open(section);
        return new Result(shown.Open, shown.Section, Sections, shown.Message);
    }

    private static IReadOnlyList<SettingsSectionView> Sections { get; } =
        SettingsSections.All.Select(s => new SettingsSectionView(s.Id, s.Title, s.Holds)).ToList();

    public sealed record Args { public string? Section { get; init; } }

    public sealed record Result(bool Open, string? Section, IReadOnlyList<SettingsSectionView> Sections, string? Message);
}

/// <summary>
/// <c>close_settings</c> — close the Settings dialog, as its Close button does. Whatever the person
/// changed there is kept: the dialog saves as it closes.
/// </summary>
public sealed class CloseSettingsTool : JsonReadTool<EmptyArgs, SettingsShown>
{
    private readonly Func<Task<SettingsShown>> _close;

    public CloseSettingsTool(Func<Task<SettingsShown>> close) => _close = close;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "close_settings",
        "Close the Settings dialog, as its Close button does — for when you opened it to show something " +
        "and the app is needed again. What the person changed there is kept; the dialog saves as it " +
        "closes. Live-applied.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    protected override Task<SettingsShown> HandleAsync(EmptyArgs args, McpToolContext context, CancellationToken ct)
        => _close();
}
