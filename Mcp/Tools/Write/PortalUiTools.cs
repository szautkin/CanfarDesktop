namespace CanfarDesktop.Mcp.Tools.Write;

/// <summary>The launch form's tabs, by the names agents use, in the form's own order.</summary>
public static class LaunchFormTabs
{
    public const string Standard = "standard";
    public const string Advanced = "advanced";
    public const string Headless = "headless";

    public static IReadOnlyList<string> All { get; } = [Standard, Advanced, Headless];

    /// <summary>Where a tab is, by name in any case; -1 for a name that is not one.</summary>
    public static int IndexOf(string? tab)
    {
        for (var i = 0; i < All.Count; i++)
            if (string.Equals(All[i], tab?.Trim(), StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }
}

/// <summary>What show_launch_form was asked for: a tab, an image to choose, or the form closed.</summary>
public sealed record LaunchFormRequest(string? Tab, string? Image, bool Close);

/// <summary>Whether the launch form is open, on which tab, with which image asked for, and why not when it is not.</summary>
public sealed record LaunchFormShown(bool Open, string? Tab, string? Message = null, string? Image = null)
{
    public static LaunchFormShown Unavailable(string message) => new(false, null, message);
}

/// <summary>
/// <c>show_launch_form</c> — open the Portal's launch form, the dialog Launch session on Active sessions
/// opens, on a tab and with an image chosen, so the person sees what is proposed and an agent can point at
/// its fields. It launches nothing: launching uses their allocation, and is launch_session's, or theirs.
///
/// <para>Verb class ViewState: it opens a dialog and chooses fields in a form, and changes no data.</para>
/// </summary>
public sealed class ShowLaunchFormTool : JsonReadTool<ShowLaunchFormTool.Args, LaunchFormShown>
{
    private readonly Func<LaunchFormRequest, Task<LaunchFormShown>> _show;

    public ShowLaunchFormTool(Func<LaunchFormRequest, Task<LaunchFormShown>> show) => _show = show;

    public override McpVerbClass VerbClass => McpVerbClass.ViewState;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "show_launch_form",
        "Open the Portal's launch form — the dialog the Launch session button on Active sessions opens — so " +
        "the person sees it and point_at_ui can point at its fields (TypeCombo, ImageCombo, LaunchButton…). " +
        "`tab` shows standard, advanced or headless; `image` chooses an image by its id, as the images card's " +
        "\"Use this image\" does: one from the catalogue on the Standard tab, any other as the Advanced tab's own " +
        "image. `close` closes it. It launches nothing — launch_session does, or the person. The Portal must be " +
        "open (navigate_to portal; it needs them signed in), and no other dialog may be.",
        """
        {"type":"object","properties":{
          "tab":{"type":"string","enum":["standard","advanced","headless"],"description":"Which tab to show."},
          "image":{"type":"string","description":"An image id to choose, e.g. images.canfar.net/skaha/astroml:24.07."},
          "close":{"type":"boolean","description":"Close the form instead (default false)."}
        },"additionalProperties":false}
        """);

    protected override Task<LaunchFormShown> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var tab = string.IsNullOrWhiteSpace(args.Tab) ? null : args.Tab.Trim().ToLowerInvariant();
        if (tab is not null && LaunchFormTabs.IndexOf(tab) < 0)
            throw new McpToolException(new InvalidArgument($"tab must be one of: {string.Join(", ", LaunchFormTabs.All)}"));

        return _show(new LaunchFormRequest(tab, string.IsNullOrWhiteSpace(args.Image) ? null : args.Image.Trim(), args.Close ?? false));
    }

    public sealed record Args
    {
        public string? Tab { get; init; }
        public string? Image { get; init; }
        public bool? Close { get; init; }
    }
}
