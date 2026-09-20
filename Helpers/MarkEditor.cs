using CanfarDesktop.Models;

namespace CanfarDesktop.Helpers;

/// <summary>
/// The field a mark is named in, as <see cref="MarkEditor"/> needs it.
///
/// An interface rather than the control, so the rules about naming a mark — Enter commits, Escape
/// leaves the mark alone, the bin deletes it, one answer per opening — can be tested without a window.
/// </summary>
public interface IMarkLabelField
{
    /// <summary>Show the field at a point in the host's coordinates, with the mark's current words.</summary>
    void Open(string current, double x, double y);

    void Close();

    /// <summary>The typed words, from the tick or from Enter.</summary>
    event Action<string>? Committed;

    /// <summary>The bin.</summary>
    event Action? Deleted;

    /// <summary>Escape. The mark is left exactly as it was.</summary>
    event Action? Cancelled;
}

/// <summary>
/// What a viewer has to tell <see cref="MarkEditor"/> about itself.
///
/// Deliberately small, and deliberately free of any UI framework. Everything a person does to a mark —
/// press, drag, resize, name, delete, restyle, pick one out of a list — is the same act on a flat image
/// and inside a cube, so none of it is here. What genuinely differs is only this: where a mark lands on
/// screen, and what a press MEANS in the viewer's own coordinates.
/// </summary>
public interface IMarkCanvas
{
    /// <summary>The file these marks belong to, or null when nothing is open.</summary>
    string? Target { get; }

    /// <summary>
    /// The view on screen. Asked for per use rather than held, because the transform moves under it —
    /// a cached surface draws marks where the image used to be.
    /// </summary>
    IAnnotationSurface Surface { get; }

    /// <summary>The field a mark is named in.</summary>
    IMarkLabelField Label { get; }

    /// <summary>
    /// The anchor a press means.
    ///
    /// <paramref name="moving"/> is the mark being dragged, or null for a new one — and it is the whole
    /// reason this is a callback rather than a coordinate conversion. A flat image keeps the mark in the
    /// space it was pinned in, so a sky mark stays a sky mark; a cube keeps the mark on ITS channel
    /// rather than moving it to whichever one the scrubber happens to show.
    /// </summary>
    AnnotationAnchor? AnchorFor(double x, double y, Annotation? moving);

    /// <summary>A point on the view, in the coordinates the label field is placed in.</summary>
    (double X, double Y) ToLabelHost(double x, double y);

    /// <summary>Draw these marks, with these picked out. The viewer owns its layers; this owns the set.</summary>
    void Draw(IReadOnlyList<Annotation> marks, string? selectedId, string? editingId);

    /// <summary>
    /// Bring a mark into view, if the viewer can. Selecting a row in the list is how a mark whose
    /// subject is off screen is found again, and only the viewer knows what "off screen" means to it.
    /// </summary>
    void Reveal(Annotation mark);

    /// <summary>
    /// Run something after the current input event has finished being handled.
    ///
    /// Opening the naming field inline would put a text box up in the middle of the pointer event that
    /// created the mark, and the press that follows it lands somewhere nobody predicted.
    /// </summary>
    void Later(Action what);
}

/// <summary>
/// Marks on a viewer: the set, the gestures that make and change them, and the field they are named in.
///
/// One of these serves the FITS canvas and the cube. It used to be two near-identical files of about six
/// hundred lines each, and the cost of that was not the duplication — it was the DRIFT. Every fix landed
/// in one viewer: the cube learnt to keep a moved mark on its own channel, to take the style controls'
/// colour, to open the naming field when a drag finished; the FITS canvas learnt some of it, later, and
/// not all. A person who had used one then found the other subtly wrong.
///
/// So the rule is that this class holds everything that is the same, and <see cref="IMarkCanvas"/> holds
/// the two things that genuinely are not. It names no UI framework, which is what lets the rules above
/// be tested rather than only looked at.
/// </summary>
public sealed class MarkEditor
{
    private readonly IMarkCanvas _canvas;
    private readonly IMarkStore? _store;
    private readonly IMarkStylePreference _style;

    private List<Annotation> _marks = [];
    private string? _loadedTarget;
    private MarkGrab _grab = new MarkGrab.None();

    /// <summary>
    /// How far the pointer may travel and still count as a click rather than a drag.
    ///
    /// A press on a mark both picks it out and takes hold of it, so "click it again to let it go" can
    /// only be decided on RELEASE — deciding it on the press would mean a selected mark could never
    /// be dragged at all. A few pixels of slop, because a mouse shifts a little under a finger, and a
    /// click that silently became a one-pixel drag would leave the mark selected for no visible reason.
    /// </summary>
    private const double ClickSlop = 3.0;

    private double _pressX, _pressY;
    private bool _pressMoved;

    /// <summary>The press landed on the mark that was ALREADY picked out — so releasing lets it go.</summary>
    private bool _pressOnSelected;
    private bool _fieldWired;

    public MarkEditor(IMarkCanvas canvas, IMarkStore? store, IMarkStylePreference style)
    {
        _canvas = canvas;
        _store = store;
        _style = style;
    }

    /// <summary>Raised whenever the set, the selection or the pencil moves. The panel listens.</summary>
    public event Action? Changed;

    /// <summary>
    /// What the next mark should look like, when something on screen has an opinion — the style row.
    /// Null from it, or unset, and the stored preference decides.
    /// </summary>
    public Func<MarkStyle?>? StyleSource { get; set; }

    /// <summary>The pencil. While it is armed, a press on empty canvas draws rather than panning.</summary>
    public bool DrawArmed { get; private set; }

    /// <summary>What a newly drawn mark will be.</summary>
    public AnnotationKind Kind { get; private set; } = AnnotationKind.Circle;

    /// <summary>The mark picked out — whose grips are live, and whose style the row shows.</summary>
    public string? SelectedId { get; private set; }

    /// <summary>The mark whose label field is open, if any.</summary>
    public string? EditingId { get; private set; }

    public IReadOnlyList<Annotation> Marks => _marks;

    /// <summary>The selected mark, or null.</summary>
    public Annotation? Selected => _marks.FirstOrDefault(a => a.Id == SelectedId);

    // ── Drawing ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Draw what is there. Called from every pan, zoom, orbit and channel change — the marks move with
    /// the view — so it also picks up a file that has changed underneath.
    /// </summary>
    public void Render()
    {
        Reload();
        _canvas.Draw(_marks, SelectedId, EditingId);
    }

    /// <summary>Load this file's marks if the file has changed under us.</summary>
    private void Reload()
    {
        var target = _canvas.Target;
        if (string.Equals(target, _loadedTarget, StringComparison.Ordinal)) return;

        _loadedTarget = target;
        EditingId = SelectedId = null;
        _marks = target is null || _store is null ? [] : _store.LoadFor(target).ToList();
    }

    /// <summary>
    /// Re-read and redraw, optionally picking a mark out. Called after an agent has changed something:
    /// the store is the truth, and this view had a copy of it.
    /// </summary>
    public bool Refresh(string target, string? selectId)
    {
        if (_canvas.Target is not { } mine || !string.Equals(mine, target, StringComparison.OrdinalIgnoreCase))
            return false;

        _marks = _store?.LoadFor(target).ToList() ?? [];

        // This IS a load for that target, so say so. Otherwise the render below reloads a second time
        // and clears the selection this was called to set.
        _loadedTarget = target;
        if (selectId is not null) SelectedId = selectId;

        Render();

        // An agent's mark has to appear in the list too, not only on the image — the list is where a
        // person sees WHAT it marked and that an agent made it.
        Announce();
        return true;
    }

    private void Save()
    {
        if (_canvas.Target is not { } target || _store is null) return;

        try
        {
            _store.SaveFor(target, _marks);
        }
        catch (Exception ex)
        {
            // A save that quietly did nothing loses a drawing, and they find out the next time they open
            // the file. Nothing here can put up a dialog, so it goes to the log and the marks stay on
            // screen — they have not lost them yet.
            System.Diagnostics.Debug.WriteLine($"Could not save marks for {target}: {ex.Message}");
        }
    }

    private void Announce()
    {
        try { Changed?.Invoke(); }
        catch { /* a listener that throws must not take the drawing down with it */ }
    }

    // ── Gestures ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Whether the marks want this press. True means the view must not pan or orbit.
    ///
    /// Asked before the pan handling, always: a press that takes hold of a mark and also starts a pan
    /// drags the image out from under the mark being moved.
    /// </summary>
    public bool TryBegin(double x, double y)
    {
        if (_canvas.Target is null) return false;

        // Before anything is added, not only when something is drawn. The set is loaded lazily, and a
        // load that happened AFTER a mark was added would read the file back over it — which is a mark
        // that vanishes the instant it is drawn. It never bit in the app only because opening a file
        // renders first; that is an ordering to remove, not to rely on.
        Reload();

        var surface = _canvas.Surface;
        var wasSelected = SelectedId;

        _pressX = x;
        _pressY = y;
        _pressMoved = false;
        _pressOnSelected = false;

        _grab = AnnotationGeometry.GrabAt(_marks, surface, SelectedId, DrawArmed, x, y);

        switch (_grab)
        {
            case MarkGrab.None:
                // Nothing of ours is under the pointer, so the press belongs to the canvas. It still
                // means something here: pressing away from the marks is how a person puts one down.
                if (wasSelected is not null)
                {
                    SelectedId = null;
                    EndEditing();
                    Render();
                    Announce();
                }
                return false;

            case MarkGrab.Place:
                if (_canvas.AnchorFor(x, y, null) is not { } anchor) return false;

                // Placed WITH a size, then dragged to the size wanted: a mark that appeared with no
                // extent would be invisible until the drag ended, and a drag that starts on nothing
                // looks like it did nothing.
                var mark = new Annotation
                {
                    Id = "m" + Guid.NewGuid().ToString("N")[..8],
                    Kind = Kind,
                    Anchor = anchor,
                    Extent = Kind.NeedsExtent()
                        ? Extent.Square(AnnotationGeometry.HalfFromDrag(
                            surface, anchor, AnnotationGeometry.InitialHalfPixels))
                        : null,
                    Author = MarkAuthor.User,
                    // What the style controls say. A style chosen and then not applied to the next mark
                    // is a control that appears to do nothing.
                    Style = PendingStyle(),
                    CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
                };

                _marks.Add(mark);
                EditingId = SelectedId = mark.Id;
                _grab = mark.Extent is null ? new MarkGrab.None() : new MarkGrab.Resize(mark.Id);
                Render();

                // A shape with no size to drag is finished the moment it lands, so it can be named now.
                // The others are named when the drag that sizes them ends — see End().
                if (mark.Extent is null) _canvas.Later(() => BeginLabelEdit(mark.Id));

                return true;

            case MarkGrab.Move move:
                _pressOnSelected = string.Equals(wasSelected, move.Id, StringComparison.Ordinal);
                SelectedId = move.Id;
                Render();
                Announce();
                return true;

            case MarkGrab.Resize resize:
                SelectedId = resize.Id;
                Announce();
                return true;

            default:
                return false;
        }
    }

    /// <summary>Continue a move or a resize. True while the marks own the pointer.</summary>
    public bool Continue(double x, double y)
    {
        var surface = _canvas.Surface;

        if (!_pressMoved &&
            (Math.Abs(x - _pressX) > ClickSlop || Math.Abs(y - _pressY) > ClickSlop))
            _pressMoved = true;

        switch (_grab)
        {
            case MarkGrab.Move move:
            {
                var index = _marks.FindIndex(a => a.Id == move.Id);
                if (index < 0) return false;

                // Where the shape was taken hold of is subtracted, so it does not jump to centre itself
                // under the pointer the moment it starts moving.
                if (_canvas.AnchorFor(x - move.GrabDx, y - move.GrabDy, _marks[index]) is not { } anchor) return true;

                _marks[index] = _marks[index] with { Anchor = anchor };
                Render();
                return true;
            }

            case MarkGrab.Resize resize:
            {
                var index = _marks.FindIndex(a => a.Id == resize.Id);
                if (index < 0) return false;

                var half = AnnotationGeometry.ResizeHalf(_marks[index], surface, x, y);
                if (half is null) return true;

                _marks[index] = _marks[index] with { Extent = Extent.Square(half.Value) };
                Render();
                return true;
            }

            default:
                return false;
        }
    }

    /// <summary>Finish a gesture and persist. True if the marks had the pointer.</summary>
    public bool End()
    {
        if (_grab is MarkGrab.None) return false;

        // A mark that was just drawn goes straight into being named. Every shape has a label — a circle
        // round something unnamed says "look here" and nothing else, and asking for the words is the
        // difference between a mark and an annotation. Sizing it is what finishes drawing it, so this
        // is where the field opens.
        var justDrawn = _grab is MarkGrab.Resize resize && resize.Id == EditingId ? resize.Id : null;

        // Clicking the mark that was already picked out lets it go. Only on a click: the same press
        // is how a mark is dragged, so a gesture that moved has said what it meant already.
        var letGo = _pressOnSelected && !_pressMoved && _grab is MarkGrab.Move;

        _grab = new MarkGrab.None();
        _pressOnSelected = false;
        _pressMoved = false;

        if (letGo)
        {
            SelectedId = null;
            EndEditing();
        }

        // Anything that failed its own validation during the drag — a shape dragged to nothing — is
        // dropped rather than stored: the store would refuse it, and a mark that is there until you
        // reopen the file is worse than one that never appeared.
        _marks.RemoveAll(a => a.Validate() is not null);

        Save();
        Render();
        Announce();

        if (justDrawn is not null && _marks.Any(a => a.Id == justDrawn))
            _canvas.Later(() => BeginLabelEdit(justDrawn));

        return true;
    }

    /// <summary>A double-press on a mark relabels it.</summary>
    public bool TryRelabelAt(double x, double y)
    {
        if (_canvas.Target is null) return false;
        if (AnnotationGeometry.AnnotationAt(_marks, _canvas.Surface, x, y) is not { } id) return false;

        SelectedId = id;
        BeginLabelEdit(id);
        return true;
    }

    // ── Naming ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>Type a mark's label, in a field over the mark itself.</summary>
    public void BeginLabelEdit(string id)
    {
        var mark = _marks.FirstOrDefault(a => a.Id == id);
        if (mark is null) return;
        if (_canvas.Surface.Project(mark.Anchor) is not { } at) return;

        WireField();

        EditingId = id;
        Render();

        // The mark's position is in the VIEW's coordinates; the field hangs off its host. One
        // translation, so the same call works over a flat image and over a cube.
        var (hx, hy) = _canvas.ToLabelHost(at.X, at.Y);

        // Below the mark, and never off the left or top edge — a field half outside its host is one you
        // cannot type in.
        _canvas.Label.Open(mark.Text, Math.Max(0, hx - 60), Math.Max(0, hy + 16));
    }

    private void WireField()
    {
        if (_fieldWired) return;
        _fieldWired = true;

        _canvas.Label.Committed += text =>
        {
            if (EditingId is { } id && _marks.FindIndex(a => a.Id == id) is >= 0 and var at)
                _marks[at] = _marks[at] with { Text = text };

            CloseField();
        };

        _canvas.Label.Deleted += () =>
        {
            if (EditingId is { } id)
            {
                _marks.RemoveAll(a => a.Id == id);
                if (SelectedId == id) SelectedId = null;
            }

            CloseField();
        };

        // Escape leaves the mark exactly as it was — including a brand-new one, which keeps whatever it
        // already had rather than being thrown away. Drawing it was deliberate; not naming it yet is not
        // a reason to lose it.
        _canvas.Label.Cancelled += CloseField;
    }

    private void CloseField()
    {
        _canvas.Label.Close();
        EditingId = null;

        // A mark still without the words it needs cannot be stored, so it goes rather than lingering
        // until the next load quietly drops it.
        _marks.RemoveAll(a => a.Validate() is not null);
        Save();
        Render();
        Announce();
    }

    /// <summary>Give up naming, keeping whatever is valid. Called when the pencil is put down.</summary>
    public void EndEditing()
    {
        _canvas.Label.Close();
        EditingId = null;
        _marks.RemoveAll(a => a.Validate() is not null);
        Save();
        Render();
    }

    // ── What the panel asks for ─────────────────────────────────────────────────────────────────

    /// <summary>Arm or disarm the pencil.</summary>
    public void SetDrawArmed(bool armed)
    {
        DrawArmed = armed;

        // Putting the pencil down commits whatever was being named. Left open, a mark with no words yet
        // is one that cannot be stored and quietly disappears on the next load.
        if (!armed) EndEditing();
        Announce();
    }

    public void SetKind(AnnotationKind kind) => Kind = kind;

    /// <summary>
    /// Let go of whatever is picked out, if this editor is showing that file.
    ///
    /// Separate from <see cref="Refresh"/> because that one's null selectId means "leave the selection
    /// alone" — every caller that redraws after a change relies on it. Clearing is a third thing, and
    /// a string cannot say three things.
    /// </summary>
    public bool Deselect(string target)
    {
        if (_canvas.Target is not { } mine || !string.Equals(mine, target, StringComparison.OrdinalIgnoreCase))
            return false;

        Select(null);
        return true;
    }

    /// <summary>Pick a mark out, and go to it if the viewer can.</summary>
    public void Select(string? id)
    {
        SelectedId = id;

        // Selecting a row is how a mark is found again after moving away from it, so this goes to the
        // mark rather than only highlighting the row.
        if (id is not null && _marks.FirstOrDefault(a => a.Id == id) is { } mark) _canvas.Reveal(mark);

        Render();
        Announce();
    }

    /// <summary>
    /// Apply a style to the selected mark, or remember it for the next one.
    ///
    /// Acting on the selection when there is one and on the next mark otherwise is how every drawing
    /// application behaves, and avoids a "preferences for new marks" screen nobody would find.
    /// </summary>
    public void ApplyStyle(MarkStyle style)
    {
        if (SelectedId is { } id && _marks.FindIndex(a => a.Id == id) is >= 0 and var at)
        {
            _marks[at] = _marks[at] with { Style = style };
            Save();
            Render();
        }
        else
        {
            _style.Default = style;
        }
    }

    public void Delete(string id)
    {
        _marks.RemoveAll(a => a.Id == id);
        if (SelectedId == id) SelectedId = null;
        Save();
        Render();
        Announce();
    }

    /// <summary>Remove the mark that is picked out, if there is one.</summary>
    public void DeleteSelected()
    {
        if (SelectedId is { } id) Delete(id);
    }

    public void ClearAll()
    {
        _marks.Clear();
        SelectedId = EditingId = null;
        Save();
        Render();
        Announce();
    }

    /// <summary>
    /// What the next mark will look like: the style row when it is on screen, the stored default
    /// otherwise.
    /// </summary>
    public MarkStyle PendingStyle() => StyleSource?.Invoke() ?? _style.Default;
}
