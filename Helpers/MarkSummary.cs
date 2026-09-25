using System.Globalization;
using CanfarDesktop.Models;

namespace CanfarDesktop.Helpers;

/// <summary>One mark as a row in the Marks list: what it says, and where and whose it is.</summary>
public sealed record MarkLine(string Id, string Title, string Detail, bool ByAgent);

/// <summary>
/// Turning marks into the rows of the Marks list.
///
/// Separated from the panel because the wording is the part worth being sure of — a row that says
/// nothing about WHERE a mark is cannot do the job the list exists for, which is finding a mark whose
/// subject is off screen — and a control is the one thing a test cannot make.
///
/// Takes its translations through <see cref="Translate"/> rather than calling <c>Loc</c>, following
/// <c>ActivitySummary</c> and <c>AiGuideCatalog</c>: the resource loader needs a packaged app, and this
/// has to be testable without one. Unset, it answers in English.
/// </summary>
public static class MarkSummary
{
    /// <summary>Resolves a resource key, or returns null to fall back to the English written here.</summary>
    public static Func<string, string?>? Translate { get; set; }

    private static string T(string key, string fallback) => Translate?.Invoke(key) ?? fallback;

    /// <summary>What kind of mark it is, in the words the shape picker uses.</summary>
    public static string KindLabel(AnnotationKind kind) => kind switch
    {
        AnnotationKind.Rect => T("Mark_KindBox", "box"),
        AnnotationKind.Circle => T("Mark_KindCircle", "circle"),
        AnnotationKind.Callout => T("Mark_KindCallout", "callout"),
        _ => T("Mark_KindText", "text"),
    };

    /// <summary>
    /// The row's headline: what the mark says, or its kind in brackets when it says nothing yet.
    ///
    /// Brackets rather than a blank row, because a mark with no label is still a mark somebody drew and
    /// still has to be findable — an empty row is one you cannot click on purpose.
    /// </summary>
    public static string Title(Annotation mark)
        => mark.Text.Trim().Length > 0 ? mark.Text.Trim() : $"({KindLabel(mark.Kind)})";

    /// <summary>
    /// Where it is, in the space it is actually pinned in.
    ///
    /// Named rather than bare numbers: "512, 384" is three different places depending on whether it is
    /// a pixel, a degree or a voxel, and the whole reason a mark carries its space is that they do not
    /// convert to each other without the file.
    /// </summary>
    public static string Place(AnnotationAnchor anchor)
    {
        var c = CultureInfo.CurrentCulture;

        return anchor.Space switch
        {
            AnchorSpace.ImagePixel => string.Format(c, T("Mark_PlacePixel", "pixel {0:0}, {1:0}"), anchor.X, anchor.Y),
            AnchorSpace.Sky => string.Format(c, T("Mark_PlaceSky", "{0:0.0000}°, {1:0.0000}°"), anchor.X, anchor.Y),
            _ => string.Format(c, T("Mark_PlaceVoxel", "voxel {0:0}, {1:0}, ch {2:0}"), anchor.X, anchor.Y, anchor.Z),
        };
    }

    /// <summary>
    /// The row's second line: what it is, where it is, and — for an agent's — whose it is.
    ///
    /// The author is shown because an agent's marks and a person's sit in the same list, and deleting
    /// someone else's work by mistake is the thing to prevent. Only the agent's are called out: a list
    /// where every row ends "by you" says nothing, and the marks a person drew are the unremarkable case.
    /// </summary>
    public static string Describe(Annotation mark)
    {
        var body = $"{KindLabel(mark.Kind)} — {Place(mark.Anchor)}";

        return mark.Author == MarkAuthor.Agent
            ? string.Format(CultureInfo.CurrentCulture, T("Mark_ByAgent", "{0} — by the agent"), body)
            : body;
    }

    /// <summary>
    /// The rows, in the order the marks are held, optionally narrowed by what was typed in the filter.
    ///
    /// The filter matches the words AND the place, so both "NGC" and "512" find something — somebody
    /// looking through thirty marks is as likely to remember where one was as what they called it.
    /// </summary>
    public static IReadOnlyList<MarkLine> Lines(IEnumerable<Annotation> marks, string? filter = null)
    {
        var needle = (filter ?? string.Empty).Trim();

        return marks
            .Select(m => new MarkLine(m.Id, Title(m), Describe(m), m.Author == MarkAuthor.Agent))
            .Where(line => needle.Length == 0
                || line.Title.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || line.Detail.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>How many there are, or null when there are none — a "0 marks" caption is furniture.</summary>
    public static string? Count(int total)
        => total == 0 ? null : string.Format(CultureInfo.CurrentCulture, T("Mark_Count", "{0} marks"), total);

    /// <summary>
    /// The count line when the file has other extensions with marks of their own.
    ///
    /// <para>Marks belong to one extension, and the list shows the one on screen — so on a forty-chip
    /// mosaic, marks drawn on another chip are invisible from here. This says they exist without
    /// mixing forty chips into one list; the extension list beside it is how to get to them.</para>
    ///
    /// <para>Null only when there is nothing to say at all. Marks elsewhere and none here is exactly
    /// the case worth saying out loud: an empty list would otherwise read as "no marks on this file".</para>
    /// </summary>
    public static string? Count(int here, int elsewhere)
    {
        if (elsewhere <= 0) return Count(here);

        var others = string.Format(CultureInfo.CurrentCulture,
            T("Mark_CountOtherExtensions", "{0} on other extensions"), elsewhere);
        return here == 0 ? others : $"{Count(here)} · {others}";
    }
}
