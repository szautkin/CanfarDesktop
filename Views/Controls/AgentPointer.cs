using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Views.Controls;

/// <summary>
/// The app pointing at one of its own controls, because an agent asked it to.
///
/// <para>An agent can already do nearly everything a person can, which leaves one thing it could not
/// do at all: show somebody WHERE something is. The answer to "where do I set the stretch?" was a
/// paragraph describing a route through panels that the person then had to follow by eye. This makes
/// the answer the app itself, with a tip whose tail touches the control in question.</para>
///
/// <para>A <see cref="TeachingTip"/> rather than anything hand-drawn: it already knows how to put a
/// tail on the side that has room, how to stay on screen near an edge, and how to be dismissed — and
/// it is what the rest of Windows uses for exactly this, so it looks like the operating system
/// pointing rather than like the app drawing on itself.</para>
///
/// <para>Which control was meant is decided in <see cref="UiPointer"/>, away from any window, because
/// that is the part with rules in it. This half is the walking and the showing.</para>
/// </summary>
public static class AgentPointer
{
    /// <summary>
    /// Everything on screen that can be pointed at, in the order it appears in the tree.
    ///
    /// <para>Only what is actually VISIBLE. A control on a collapsed panel or an unloaded page is not
    /// somewhere a person can be sent — pointing at it would put a tip in the corner attached to
    /// nothing, which reads as the app being broken rather than as the control being elsewhere.</para>
    /// </summary>
    public static IReadOnlyList<UiPointer.Target> Targets(DependencyObject? root)
    {
        var found = new List<UiPointer.Target>();
        if (root is null) return found;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Walk(root, found, seen, depth: 0);
        return found;
    }

    /// <summary>
    /// Depth is bounded because a visual tree can be deep and this runs on the UI thread while an
    /// agent waits. Sixty levels is far past anything in this app and still cheap.
    /// </summary>
    private const int MaxDepth = 60;

    private static void Walk(
        DependencyObject node, List<UiPointer.Target> found, HashSet<string> seen, int depth)
    {
        if (depth > MaxDepth) return;

        if (node is FrameworkElement element)
        {
            // An invisible subtree is not somewhere a person can be sent, and its children are not
            // either — so the whole branch is skipped rather than each leaf being tested.
            if (element.Visibility != Visibility.Visible) return;

            if (Pointable(element) is { } target && seen.Add(target.Id)) found.Add(target);

            // A primitive's insides are its TEMPLATE, not its content, and a template is full of
            // named parts nobody would ever ask for. Walking into a NumberBox turned up its inner
            // "InputBox"; because ids are deduplicated, the first one won and every other number
            // field on the page was dropped in favour of it. Stopping here is what makes the list
            // the page's controls instead of the framework's.
            if (IsPrimitive(element)) return;
        }

        var children = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < children; i++)
            Walk(VisualTreeHelper.GetChild(node, i), found, seen, depth + 1);
    }

    /// <summary>
    /// A control a person acts on directly, with nothing inside it worth pointing at separately.
    ///
    /// Containers are deliberately absent: a ListView's rows hold real buttons, an Expander's content
    /// is the page's own, and stopping at those would hide most of what is on screen.
    /// </summary>
    private static bool IsPrimitive(FrameworkElement element)
        => element is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase
            or TextBox or PasswordBox or RichEditBox or AutoSuggestBox or NumberBox
            or ComboBox or Slider or ToggleSwitch or CalendarDatePicker or DatePicker or TimePicker
            or ProgressBar or ProgressRing or Microsoft.UI.Xaml.Controls.Primitives.ScrollBar;

    /// <summary>
    /// Things that are on screen and are not somewhere to send a person: scrolling machinery, bare
    /// presenters, and the hint itself, which has no business being a target for the next hint.
    /// </summary>
    private static bool NeverPointable(FrameworkElement element)
        => element.Name.StartsWith("PART_", StringComparison.Ordinal)
           || element is TeachingTip
           || element.GetType().Name is "ScrollViewer" or "ScrollView" or "ScrollBar"
               or "ContentPresenter" or "ItemsPresenter" or "ContentControl" or "ItemsView"
               or "Border" or "Panel";

    /// <summary>
    /// Whether this element is a thing somebody could be shown, and what to call it.
    ///
    /// <para>It needs a name to be asked for by, and it has to be something a person acts on. The
    /// templates inside a control are full of named parts — every button has a background border
    /// called something — so a bare name is not enough on its own; it has to be a control, or an
    /// element somebody has deliberately given an automation name, which is the same judgement a
    /// screen reader makes.</para>
    /// </summary>
    private static UiPointer.Target? Pointable(FrameworkElement element)
    {
        var automation = Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(element);
        var named = !string.IsNullOrWhiteSpace(element.Name);
        var announced = !string.IsNullOrWhiteSpace(automation);

        if (!named && !announced) return null;
        if (element is not Control && !announced) return null;
        if (NeverPointable(element)) return null;

        // A control with no size is laid out but not on screen — a collapsed column, a zero-height row.
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0) return null;

        var id = named ? element.Name : automation;
        return new UiPointer.Target(id, element.GetType().Name, Label(element, automation));
    }

    /// <summary>
    /// What the control says to a person, which is what an agent will usually have been told.
    ///
    /// The tooltip sits between the automation name and the content on purpose: most icon-only
    /// buttons here have one, and it is written for a person, so it is the best words available
    /// once the content turns out to be a glyph.
    /// </summary>
    private static string? Label(FrameworkElement element, string? automation)
        => Words(automation)
           ?? Words(ToolTipService.GetToolTip(element) as string)
           ?? element switch
           {
               TextBox box => Text(box.PlaceholderText, box.Header as string),
               ContentControl { Content: string words } => Words(words),
               ContentControl { Content: FrameworkElement inner } => Words(FirstText(inner)),
               _ => null,
           };

    private static string? Text(params string?[] candidates)
        => candidates.Select(Words).FirstOrDefault(s => s is not null);

    /// <summary>
    /// Usable words, or nothing.
    ///
    /// An icon-only button's "content" is a Private Use Area character — U+E721, U+E768 — which is
    /// not a label an agent can be told to look for and not one anybody can type. Rejected here so
    /// the control falls back to its name, which at least reads as English.
    /// </summary>
    private static string? Words(string? text)
        => string.IsNullOrWhiteSpace(text) || UiPointer.IsGlyphOnly(text) ? null : text.Trim();

    /// <summary>The first words inside a control's content — a button's caption beside its icon.</summary>
    private static string? FirstText(DependencyObject node, int depth = 0)
    {
        if (depth > 4) return null;
        if (node is TextBlock { Text: var text } && !string.IsNullOrWhiteSpace(text)) return text.Trim();

        var children = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < children; i++)
            if (FirstText(VisualTreeHelper.GetChild(node, i), depth + 1) is { } found) return found;

        return null;
    }

    /// <summary>The element behind an id, or null when it is no longer on screen.</summary>
    public static FrameworkElement? Find(DependencyObject? root, string id)
    {
        if (root is null || string.IsNullOrWhiteSpace(id)) return null;

        if (root is FrameworkElement element)
        {
            if (element.Visibility != Visibility.Visible) return null;

            if (Pointable(element) is { } target &&
                string.Equals(target.Id, id, StringComparison.OrdinalIgnoreCase))
                return element;
        }

        var children = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < children; i++)
            if (Find(VisualTreeHelper.GetChild(root, i), id) is { } found) return found;

        return null;
    }

    /// <summary>
    /// The hints that are up, so several can be shown at once and all of them can be taken away
    /// together when the page changes.
    /// </summary>
    private static readonly List<TeachingTip> Live = [];

    /// <summary>
    /// The last hint has gone, however it went — closed, timed out, or cleared by a page change.
    ///
    /// <para>This is what lets a guided tour move at the person's pace instead of on a timer. An
    /// agent puts up three hints, says nothing more, and waits; when they have read all three and
    /// closed them, the tour moves on. Any other design either hurries somebody who is still reading
    /// or leaves them looking at a finished screen wondering what they missed.</para>
    ///
    /// <para>Raised only on the transition to empty, so closing the second of three is silent.</para>
    /// </summary>
    public static event Action? AllClosed;

    /// <summary>
    /// Point at it: bring it into view, then put a tip on it. Returns how many are now up.
    ///
    /// <para>Brought into view first because a control inside a scrolled panel may be perfectly
    /// present and forty pixels below the fold, and a tail pointing off the edge of a scroller is
    /// worse than no tip at all.</para>
    ///
    /// <para>One tip per control: pointing at the same thing twice replaces rather than stacks, since
    /// two tails on one button is two things claiming to be it. Pointing at a DIFFERENT control adds
    /// another, because walking somebody through three controls is three tips.</para>
    /// </summary>
    public static int Show(
        Panel host, FrameworkElement target, string? title, string message, double seconds)
    {
        Close(Live.FirstOrDefault(t => ReferenceEquals(t.Target, target)));

        target.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = true });

        var tip = new TeachingTip
        {
            Target = target,
            Title = title ?? string.Empty,
            Subtitle = message,
            PreferredPlacement = TeachingTipPlacementMode.Auto,

            // Not light-dismiss: that is what puts the close button in the corner, and it is what
            // lets a tip survive the very click a person makes to act on the control it points at.
            IsLightDismissEnabled = false,
        };

        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tip, title ?? message);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        timer.Tick += (_, _) => Close(tip);

        // Reading it should not race it. Once somebody has moved onto the tip they have said they
        // are paying attention, so the countdown stops for good and the close button is how it goes
        // — a tip that vanished mid-sentence because the mouse was there is the worst of both.
        tip.PointerEntered += (_, _) => timer.Stop();

        tip.CloseButtonClick += (sender, _) => Close(sender as TeachingTip);
        tip.Closed += (sender, _) => Forget(sender as TeachingTip);

        host.Children.Add(tip);
        Live.Add(tip);
        tip.IsOpen = true;
        timer.Start();

        return Live.Count;
    }

    /// <summary>Take one away.</summary>
    private static void Close(TeachingTip? tip)
    {
        if (tip is null) return;

        tip.IsOpen = false;   // Closed then removes it from the tree
        Forget(tip);
    }

    /// <summary>Drop a tip from the live set and out of the tree, however it came to be closed.</summary>
    private static void Forget(TeachingTip? tip)
    {
        if (tip is null) return;

        var had = Live.Count;
        Live.Remove(tip);
        if (tip.Parent is Panel host) host.Children.Remove(tip);

        // On the way to empty, and only then: closing the second of three says nothing.
        if (had > 0 && Live.Count == 0) Announce();
    }

    /// <summary>A listener that throws must not take the hints down with it.</summary>
    private static void Announce()
    {
        try { AllClosed?.Invoke(); }
        catch { /* the tour is not worth a crash */ }
    }

    /// <summary>
    /// Take them all away, now.
    ///
    /// Called when the app changes page: a hint points at a control on the page being left, and a
    /// tail reaching across a screen that has changed underneath it points at whatever is now in
    /// that spot.
    /// </summary>
    public static void CloseAll()
    {
        // No announcement of its own: closing the last of these goes through Forget, which already
        // raises on the transition to empty. Doing both sent the event TWICE for every page change —
        // visible in the log as pairs sharing a timestamp, against a lone event when a tip simply
        // timed out. A tour reading that as its cue would have advanced two screens at once.
        foreach (var tip in Live.ToList()) Close(tip);
        Live.Clear();
    }
}
