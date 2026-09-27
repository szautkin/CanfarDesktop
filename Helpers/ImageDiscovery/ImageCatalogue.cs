using CanfarDesktop.Models;
using CanfarDesktop.Models.ImageDiscovery;

namespace CanfarDesktop.Helpers.ImageDiscovery;

/// <summary>
/// The CANFAR images as someone choosing what to run sees them: only those a launch can start, narrowed
/// by session type and then by project. The Portal's images card, Find by package and the agents' image
/// listing ask the same questions of it, so none offers what another would not. Pure, and the rules of
/// Verbinal for Linux's images card.
///
/// <para>"All" is the empty string, in both filters: the absence of a filter, not a type or a project,
/// so an image whose labels name no type still has somewhere to appear.</para>
/// </summary>
public static class ImageCatalogue
{
    /// <summary>What "All" is, in either filter.</summary>
    public const string All = "";

    /// <summary>
    /// Whether a launch tab can start it: one of its types is one a tab offers, or the person added it.
    /// The platform's catalogue is not a list of things to start — some 77 of its images are desktop-app
    /// and nothing else, applications published inside a desktop session. They were a fifth of the card,
    /// and inspecting one spent a probe job on an image no tab would ever offer. An image the person
    /// added is kept whatever its labels say: they went and found it, and Advanced launches it.
    /// </summary>
    public static bool Launchable(ParsedImage image, IReadOnlySet<string> mine)
        => mine.Contains(image.Id)
           || image.Types.Any(t => SessionTypes.Launchable.Contains(t, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// The types to offer, each once under its group (desktop-app with desktop), in the launch form's
    /// order, then any the platform adds, alphabetically.
    /// </summary>
    public static IReadOnlyList<string> Types(IEnumerable<ParsedImage> images)
        => images
            .SelectMany(i => i.Types)
            .Select(SessionTypes.Group)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(Rank)
            .ThenBy(t => t, StringComparer.Ordinal)
            .ToList();

    /// <summary>A type's place among those the launch form offers; after all of them when it is not one.</summary>
    private static int Rank(string type)
    {
        for (var i = 0; i < SessionTypes.Launchable.Count; i++)
            if (string.Equals(SessionTypes.Launchable[i], type, StringComparison.OrdinalIgnoreCase)) return i;
        return int.MaxValue;
    }

    /// <summary>Whether the image is of the chosen type's group; All is every type.</summary>
    public static bool OfType(ParsedImage image, string type)
        => type == All || image.Types.Any(t => string.Equals(SessionTypes.Group(t), type, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether the image is in the chosen project; All is every project.</summary>
    public static bool InProject(ParsedImage image, string project)
        => project == All || string.Equals(image.Project, project, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The projects among the images of the chosen type — only those with something in them, since a
    /// button that selects nothing is a dead end. Alphabetical rather than by size: a filter whose
    /// buttons change places when a project publishes a tag has to be read afresh every time.
    /// </summary>
    public static IReadOnlyList<string> Projects(IEnumerable<ParsedImage> images, string type)
        => images
            .Where(i => OfType(i, type) && i.Project.Length > 0)
            .Select(i => i.Project)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// The choice that survives a new set of choices: kept while it is offered, otherwise All. Choosing
    /// CARTA with a project chosen that has no CARTA image used to leave an empty list, its cause a
    /// pressed button in a row just rebuilt without it; All shows the CARTA images, which is what
    /// choosing CARTA meant.
    /// </summary>
    public static string Surviving(string chosen, IReadOnlyList<string> offered)
        => offered.Contains(chosen, StringComparer.OrdinalIgnoreCase) ? chosen : All;

    /// <summary>
    /// The images shown for a type and a project: inspected first, then those whose inspection failed,
    /// then those never inspected, each by id.
    /// </summary>
    public static IReadOnlyList<ParsedImage> Shown(
        IEnumerable<ParsedImage> images, string type, string project, Func<string, ImageDiscoveryStatus> status)
        => images
            .Where(i => OfType(i, type) && InProject(i, project))
            .OrderBy(i => Order(status(i.Id)))
            .ThenBy(i => i.Id, StringComparer.Ordinal)
            .ToList();

    /// <summary>Where a status sorts: inspected, failed, never inspected.</summary>
    public static int Order(ImageDiscoveryStatus status) => status switch
    {
        ImageDiscoveryStatus.Discovered => 0,
        ImageDiscoveryStatus.Failed => 1,
        _ => 2,
    };
}
