namespace CanfarDesktop.Helpers;

/// <summary>
/// Which thing on screen an agent meant, and for how long to point at it.
///
/// <para>Every other tool in this app lets an agent DO something. This one lets it show somebody
/// where a thing is — the answer to "where is that setting?" being the app itself pointing at it,
/// rather than a paragraph describing a route through menus that the person then has to follow.</para>
///
/// <para>The matching lives here, away from any window, because it is the part with rules in it: an
/// agent names a control in whatever words it has, and those words have to land on exactly one
/// element or on none. Guessing wrong points a person confidently at the wrong thing, which is worse
/// than admitting the name did not match.</para>
/// </summary>
public static class UiPointer
{
    /// <summary>How long a tip stays up when nobody says.</summary>
    public const double DefaultSeconds = 8.0;

    /// <summary>Long enough to read a short sentence; short enough not to sit there forever.</summary>
    public const double MinSeconds = 2.0;
    public const double MaxSeconds = 60.0;

    /// <summary>A tip that never leaves is a tip that has to be dismissed, so there is a ceiling.</summary>
    public static double Seconds(double? asked)
        => asked is not { } value || !double.IsFinite(value)
            ? DefaultSeconds
            : Math.Clamp(value, MinSeconds, MaxSeconds);

    /// <summary>
    /// One thing on screen that can be pointed at.
    /// </summary>
    /// <param name="Id">
    /// What an agent names it by — the element's own name, which is stable across runs and across
    /// languages. Unique within a screen.
    /// </param>
    /// <param name="Label">
    /// What it says to a person, which is localized and therefore NOT the identifier, but is very
    /// often what an agent will have been told to look for.
    /// </param>
    public readonly record struct Target(string Id, string Kind, string? Label);

    /// <summary>
    /// Whether a label is nothing but icon font.
    ///
    /// <para>A great many buttons here show only a glyph, and a glyph lives in the Private Use Area —
    /// so the "words on the button" come back as U+E721 or U+E768. That is not a label: an agent told
    /// "press the search button" cannot match it, and offering it as a candidate is offering a
    /// character nobody can type. Better to have no label and fall back to the control's name.</para>
    /// </summary>
    public static bool IsGlyphOnly(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        var meaningful = text.Where(c => !char.IsWhiteSpace(c)).ToList();
        // The Private Use Area, by code point rather than by literal: the characters themselves
        // are unrenderable and would not survive a copy between editors intact.
        return meaningful.Count > 0 && meaningful.All(c => c >= (char)0xE000 && c <= (char)0xF8FF);
    }

    /// <summary>
    /// Case, spaces, punctuation and the decorations a label carries — an ellipsis, a colon, an
    /// access-key ampersand — are not part of what somebody meant.
    /// </summary>
    public static string Normalise(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var kept = text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant);
        return new string(kept.ToArray());
    }

    /// <summary>
    /// The one target an agent meant, or null when nothing matched or several did equally well.
    ///
    /// <para>In order: the id outright, then the label outright, then a label that contains the words
    /// asked for. Each tier is resolved fully before the next is tried, so a weaker match never beats
    /// a stronger one — and a tier matching TWICE is an ambiguity rather than a coin toss, because
    /// pointing confidently at one of two "Export" buttons is how somebody ends up in the wrong
    /// dialog believing they were shown the right one.</para>
    /// </summary>
    public static string? Best(IReadOnlyList<Target> targets, string? query)
    {
        var wanted = Normalise(query);
        if (wanted.Length == 0 || targets.Count == 0) return null;

        return Only(targets.Where(t => Normalise(t.Id) == wanted))
            ?? Only(targets.Where(t => Normalise(t.Label) == wanted))
            ?? Only(targets.Where(t => Normalise(t.Label).Contains(wanted)))
            ?? Only(targets.Where(t => Normalise(t.Id).Contains(wanted)));
    }

    /// <summary>Exactly one, or nothing. Two candidates is a question, not an answer.</summary>
    private static string? Only(IEnumerable<Target> matches)
    {
        var found = matches.Take(2).ToList();
        return found.Count == 1 ? found[0].Id : null;
    }

    /// <summary>
    /// What to offer when a name did not land: the nearest things by name, then whatever else is
    /// there, so the answer to a miss is always a list an agent can choose from rather than a refusal.
    /// </summary>
    public static IReadOnlyList<Target> Suggest(
        IReadOnlyList<Target> targets, string? query, int max = 12)
    {
        if (max <= 0 || targets.Count == 0) return [];

        var wanted = Normalise(query);
        if (wanted.Length == 0) return targets.Take(max).ToList();

        // Anything sharing a run of characters with what was asked for, longest first: an agent that
        // said "export figure" should be shown "ExportFigureButton" ahead of an unrelated control.
        return targets
            .Select(t => (Target: t, Score: Overlap(wanted, Normalise(t.Id)) + Overlap(wanted, Normalise(t.Label))))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Target.Id, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .Select(x => x.Target)
            .ToList();
    }

    /// <summary>How much of <paramref name="wanted"/> appears in <paramref name="candidate"/>.</summary>
    private static int Overlap(string wanted, string candidate)
    {
        if (wanted.Length == 0 || candidate.Length == 0) return 0;
        if (candidate.Contains(wanted)) return wanted.Length * 2;

        // The longest prefix of what was asked for that appears anywhere in the candidate.
        for (var length = Math.Min(wanted.Length, candidate.Length); length >= 3; length--)
            if (candidate.Contains(wanted[..length])) return length;

        return 0;
    }
}
