namespace CanfarDesktop.Helpers;

/// <summary>
/// The sections of the Settings dialog: the id an agent passes to <c>open_settings</c>, the title a
/// person sees in the list, and what the section holds — so an agent can choose the section to show
/// without opening each one to look.
///
/// <para>The ids are the dialog's own NavigationViewItem tags; a test holds the two together.</para>
/// </summary>
public static class SettingsSections
{
    public sealed record Section(string Id, string Title, string Holds);

    public static IReadOnlyList<Section> All { get; } =
    [
        new("general", "General", "theme and language; under Advanced, the service endpoints and a connection test"),
        new("portal", "Portal", "the session type, cores, RAM and GPUs a launch starts with"),
        new("agent", "AI agent", "the MCP server, auto-apply, following agent activity, sound cues, connecting Claude, approved clients and diagnostics"),
        new("discovery", "Image discovery", "the container registry and its sign-in, for seeing what an image carries"),
        new("compute", "AI compute", "the compute image, its size and registry sign-in, for Remote Compute"),
        new("notebook", "Notebook", "the editor, autosave, the Python used and the cell timeout"),
        new("about", "About", "the version, links, and runtime details for a bug report"),
    ];

    /// <summary>The section an id or a title names, ignoring case and spacing; null when it names none.</summary>
    public static Section? Find(string? name)
    {
        var n = (name ?? string.Empty).Trim();
        return All.FirstOrDefault(s => string.Equals(s.Id, n, StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(s.Title, n, StringComparison.OrdinalIgnoreCase));
    }
}
