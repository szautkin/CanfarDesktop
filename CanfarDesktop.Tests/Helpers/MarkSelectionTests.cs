using Xunit;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// Putting a mark down again.
///
/// <para>Picking one out was reachable and letting it go was not: a press on a mark selected it, a
/// press on empty canvas left it selected, and the marks list is a ListView, which does not even
/// raise a selection change when you click the row that is already chosen. So a mark stayed picked
/// out until some other one was — and a picked-out mark is not idle, it is the one the style row
/// edits and the one the Delete key takes.</para>
///
/// <para>The awkward part is that a press on a mark BOTH picks it out and takes hold of it, so
/// "click it again to let it go" can only be decided on release. These pin that down.</para>
/// </summary>
public class MarkSelectionTests
{
    private sealed class Flat : IAnnotationSurface
    {
        public (double X, double Y)? Project(AnnotationAnchor a)
            => a.Space == AnchorSpace.ImagePixel ? (a.X, a.Y) : null;

        public double UnitsToPixels(AnnotationAnchor a) => 1.0;
    }

    private sealed class Field : IMarkLabelField
    {
        public bool IsOpen { get; private set; }
        public event Action<string>? Committed;
        public event Action? Deleted;
        public event Action? Cancelled;

        public void Open(string current, double x, double y) => IsOpen = true;
        public void Close() => IsOpen = false;
        public void Unused() { Committed?.Invoke(""); Deleted?.Invoke(); Cancelled?.Invoke(); }
    }

    private sealed class Canvas : IMarkCanvas
    {
        public string? Target { get; set; } = "image.fits";
        public IAnnotationSurface Surface { get; } = new Flat();
        public Field Label { get; } = new();
        IMarkLabelField IMarkCanvas.Label => Label;

        public string? LastSelected { get; private set; }
        public Annotation? Revealed { get; private set; }

        public AnnotationAnchor? AnchorFor(double x, double y, Annotation? moving)
            => AnnotationAnchor.ImagePixel(x, y);

        public (double X, double Y) ToLabelHost(double x, double y) => (x, y);

        public void Draw(IReadOnlyList<Annotation> marks, string? selectedId, string? editingId)
            => LastSelected = selectedId;

        public void Reveal(Annotation mark) => Revealed = mark;
        public void Later(Action what) => what();
    }

    private sealed class Store : IMarkStore
    {
        private readonly Dictionary<string, List<Annotation>> _saved = new();

        public IReadOnlyList<Annotation> LoadFor(string target)
            => _saved.TryGetValue(target, out var m) ? m.ToList() : [];

        public IReadOnlyList<Annotation> SaveFor(string target, IReadOnlyList<Annotation> marks)
            => _saved[target] = marks.ToList();
    }

    private static (MarkEditor editor, Canvas canvas) Build()
    {
        var canvas = new Canvas();
        return (new MarkEditor(canvas, new Store(), new InMemoryMarkStylePreference()), canvas);
    }

    /// <summary>
    /// Draw a box centred on (x, y), roughly 30 wide, and leave NOTHING picked out.
    ///
    /// Drawing a mark leaves it selected — that is real behaviour, covered in MarkEditorTests — but
    /// it makes a poor starting point here: the first click of a toggle test would land on a mark that
    /// was already chosen and read as the second.
    /// </summary>
    private static Annotation Draw(MarkEditor editor, double x, double y)
    {
        editor.SetDrawArmed(true);
        Assert.True(editor.TryBegin(x, y));
        editor.Continue(x + 15, y + 15);
        editor.End();
        editor.SetDrawArmed(false);

        var mark = editor.Marks[^1];
        editor.Select(null);
        return mark;
    }

    /// <summary>A press and release at one point, with no travel in between.</summary>
    private static void Click(MarkEditor editor, double x, double y)
    {
        editor.TryBegin(x, y);
        editor.End();
    }

    // ── Clicking a mark ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ClickingAMarkPicksItOut()
    {
        var (editor, _) = Build();
        var mark = Draw(editor, 100, 100);

        Click(editor, 100, 100);

        Assert.Equal(mark.Id, editor.SelectedId);
    }

    /// <summary>The whole point: the same click again lets it go.</summary>
    [Fact]
    public void ClickingTheSameMarkAgainLetsItGo()
    {
        var (editor, _) = Build();
        var mark = Draw(editor, 100, 100);

        Click(editor, 100, 100);
        Assert.Equal(mark.Id, editor.SelectedId);

        Click(editor, 100, 100);
        Assert.Null(editor.SelectedId);
    }

    /// <summary>And a third click picks it up again — it is a toggle, not a one-way latch.</summary>
    [Fact]
    public void TheToggleGoesBothWays()
    {
        var (editor, _) = Build();
        var mark = Draw(editor, 100, 100);

        Click(editor, 100, 100);
        Click(editor, 100, 100);
        Click(editor, 100, 100);

        Assert.Equal(mark.Id, editor.SelectedId);
    }

    /// <summary>
    /// The case that makes this delicate. A press on a selected mark takes hold of it, so deciding on
    /// the press would mean a selected mark could never be dragged. A gesture that MOVED has already
    /// said what it meant.
    /// </summary>
    [Fact]
    public void DraggingASelectedMarkDoesNotLetItGo()
    {
        var (editor, _) = Build();
        var mark = Draw(editor, 100, 100);
        Click(editor, 100, 100);

        editor.TryBegin(100, 100);
        editor.Continue(140, 100);
        editor.End();

        Assert.Equal(mark.Id, editor.SelectedId);
    }

    /// <summary>
    /// A mouse shifts a little under a finger. A click that silently became a one-pixel drag would
    /// leave the mark picked out for no reason a person could see.
    /// </summary>
    [Fact]
    public void AWobbleWithinTheSlopIsStillAClick()
    {
        var (editor, _) = Build();
        Draw(editor, 100, 100);
        Click(editor, 100, 100);

        editor.TryBegin(100, 100);
        editor.Continue(101, 101);
        editor.End();

        Assert.Null(editor.SelectedId);
    }

    // ── Clicking away ───────────────────────────────────────────────────────────────────────────

    /// <summary>Pressing away from the marks is the other way a person puts one down.</summary>
    [Fact]
    public void PressingEmptyCanvasLetsGo()
    {
        var (editor, _) = Build();
        Draw(editor, 100, 100);
        Click(editor, 100, 100);

        Assert.False(editor.TryBegin(400, 400), "empty canvas belongs to the viewer");
        Assert.Null(editor.SelectedId);
    }

    /// <summary>
    /// The canvas still gets the press. Letting go of a mark must not swallow the gesture, or pressing
    /// away from a selected mark would stop panning the image.
    /// </summary>
    [Fact]
    public void LettingGoDoesNotSwallowTheCanvasPress()
    {
        var (editor, _) = Build();
        Draw(editor, 100, 100);
        Click(editor, 100, 100);

        Assert.False(editor.TryBegin(400, 400));
    }

    /// <summary>The viewer is told, so the highlight actually goes away.</summary>
    [Fact]
    public void TheViewerIsRedrawnWithNothingPickedOut()
    {
        var (editor, canvas) = Build();
        Draw(editor, 100, 100);
        Click(editor, 100, 100);
        Assert.NotNull(canvas.LastSelected);

        Click(editor, 100, 100);

        Assert.Null(canvas.LastSelected);
    }

    /// <summary>Clicking a DIFFERENT mark picks that one out rather than letting go of nothing.</summary>
    [Fact]
    public void ClickingAnotherMarkMovesTheSelection()
    {
        var (editor, _) = Build();
        Draw(editor, 100, 100);
        var second = Draw(editor, 300, 300);

        Click(editor, 100, 100);
        Click(editor, 300, 300);

        Assert.Equal(second.Id, editor.SelectedId);
    }

    // ── Deselect(target) — what the MCP tool calls ──────────────────────────────────────────────

    [Fact]
    public void DeselectingByFileLetsGo()
    {
        var (editor, _) = Build();
        Draw(editor, 100, 100);
        Click(editor, 100, 100);

        Assert.True(editor.Deselect("image.fits"));
        Assert.Null(editor.SelectedId);
    }

    /// <summary>
    /// A viewer showing something else must not have its selection cleared by an agent talking about
    /// another file — the same guard Refresh already applies.
    /// </summary>
    [Fact]
    public void DeselectingAFileThisViewerIsNotShowingDoesNothing()
    {
        var (editor, _) = Build();
        var mark = Draw(editor, 100, 100);
        Click(editor, 100, 100);

        Assert.False(editor.Deselect("somewhere/else.fits"));
        Assert.Equal(mark.Id, editor.SelectedId);
    }

    // ── A viewer with no store ─────────────────────────────────────────────────

    /// <summary>
    /// The silent failure this cost us. The cube viewer was never given an annotation store, so its
    /// editor was built with a null one — and Refresh still answered TRUE on a matching file while its
    /// list stayed empty. An agent wrote a mark, the store kept it, list_cube_annotations reported it,
    /// the tool said "shown", and nothing was ever drawn, in the viewer or in an exported figure.
    ///
    /// A viewer that cannot read the marks has not shown them.
    /// </summary>
    [Fact]
    public void AViewerWithNoStoreDoesNotClaimToHaveShownAnything()
    {
        var canvas = new Canvas();
        var editor = new MarkEditor(canvas, store: null, new InMemoryMarkStylePreference());

        Assert.False(editor.Refresh("image.fits", selectId: null));
        Assert.Empty(editor.Marks);
    }

    [Fact]
    public void DeselectingWithNothingPickedOutIsStillAnAnswer()
    {
        var (editor, _) = Build();
        Draw(editor, 100, 100);

        Assert.True(editor.Deselect("image.fits"));
        Assert.Null(editor.SelectedId);
    }
}
