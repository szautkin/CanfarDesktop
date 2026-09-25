using Xunit;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// Everything a person does to a mark, on either viewer.
///
/// These rules used to live twice — once in the FITS canvas and once in the cube — and the cost was not
/// the duplication but the drift: a fix landed in one and not the other, and the two viewers slowly
/// stopped behaving alike. They are one class now, and this is what pins its behaviour.
/// </summary>
public class MarkEditorTests
{
    /// <summary>A flat surface that projects image pixels straight through.</summary>
    private sealed class Flat : IAnnotationSurface
    {
        public (double X, double Y)? Project(AnnotationAnchor a)
            => a.Space == AnchorSpace.ImagePixel ? (a.X, a.Y) : null;

        public double UnitsToPixels(AnnotationAnchor a) => 1.0;
    }

    /// <summary>A label field with no window behind it.</summary>
    private sealed class Field : IMarkLabelField
    {
        public bool IsOpen { get; private set; }
        public string Shown { get; private set; } = "";
        public (double X, double Y) At { get; private set; }

        public event Action<string>? Committed;
        public event Action? Deleted;
        public event Action? Cancelled;

        public void Open(string current, double x, double y)
        {
            IsOpen = true;
            Shown = current;
            At = (x, y);
        }

        public void Close() => IsOpen = false;

        public void Commit(string text) => Committed?.Invoke(text);
        public void Bin() => Deleted?.Invoke();
        public void Escape() => Cancelled?.Invoke();
    }

    /// <summary>A viewer, reduced to the two things a viewer actually decides.</summary>
    private sealed class Canvas : IMarkCanvas
    {
        public string? Target { get; set; } = "image.fits";
        public IAnnotationSurface Surface { get; } = new Flat();
        public Field Label { get; } = new();
        IMarkLabelField IMarkCanvas.Label => Label;

        public int Draws { get; private set; }
        public List<Annotation> LastDrawn { get; private set; } = [];
        public string? LastSelected { get; private set; }
        public string? LastEditing { get; private set; }
        public Annotation? Revealed { get; private set; }

        /// <summary>What a press means here: an image pixel, keeping a moved mark in its own space.</summary>
        public AnnotationAnchor? AnchorFor(double x, double y, Annotation? moving)
            => AnnotationAnchor.ImagePixel(x, y);

        public (double X, double Y) ToLabelHost(double x, double y) => (x, y);

        public void Draw(IReadOnlyList<Annotation> marks, string? selectedId, string? editingId)
        {
            Draws++;
            LastDrawn = marks.ToList();
            LastSelected = selectedId;
            LastEditing = editingId;
        }

        public void Reveal(Annotation mark) => Revealed = mark;

        /// <summary>Run it now: a test has no input event to wait for the end of.</summary>
        public void Later(Action what) => what();
    }

    /// <summary>A store that keeps what it is given, so saving can be observed.</summary>
    private sealed class Store : IMarkStore
    {
        public Dictionary<string, List<Annotation>> Saved { get; } = new();
        public int Saves { get; private set; }

        public IReadOnlyList<Annotation> LoadFor(string target)
            => Saved.TryGetValue(target, out var marks) ? marks.ToList() : [];

        public IReadOnlyList<Annotation> SaveFor(string target, IReadOnlyList<Annotation> marks)
        {
            Saves++;
            return Saved[target] = marks.ToList();
        }
    }

    private static (MarkEditor editor, Canvas canvas, Store store) Build()
    {
        var canvas = new Canvas();
        var store = new Store();
        return (new MarkEditor(canvas, store, new InMemoryMarkStylePreference()), canvas, store);
    }

    /// <summary>Draw a mark by pressing, dragging to size it, and letting go.</summary>
    private static Annotation DrawOne(MarkEditor editor, double x = 100, double y = 100, double toX = 130, double toY = 130)
    {
        editor.SetDrawArmed(true);
        Assert.True(editor.TryBegin(x, y));
        editor.Continue(toX, toY);
        editor.End();
        return editor.Marks[^1];
    }

    // ── Drawing ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PressingWithThePencilDownMakesAMark()
    {
        var (editor, _, _) = Build();

        var mark = DrawOne(editor);

        Assert.Single(editor.Marks);
        Assert.Equal(AnnotationKind.Circle, mark.Kind);
        Assert.Equal(MarkAuthor.User, mark.Author);
    }

    /// <summary>A press with the pencil UP is the view's, so it can pan.</summary>
    [Fact]
    public void PressingEmptyCanvasWithThePencilUpIsNotOurs()
    {
        var (editor, _, _) = Build();

        Assert.False(editor.TryBegin(100, 100));
        Assert.Empty(editor.Marks);
    }

    [Fact]
    public void WithNoFileOpenNothingIsDrawn()
    {
        var (editor, canvas, _) = Build();
        canvas.Target = null;
        editor.SetDrawArmed(true);

        Assert.False(editor.TryBegin(100, 100));
    }

    /// <summary>
    /// Placed WITH a size, then dragged to the size wanted: a mark that appeared with no extent would be
    /// invisible until the drag ended, and a drag that starts on nothing looks like it did nothing.
    /// </summary>
    [Fact]
    public void ANewMarkHasASizeBeforeTheDragBegins()
    {
        var (editor, _, _) = Build();
        editor.SetDrawArmed(true);
        editor.TryBegin(100, 100);

        Assert.NotNull(editor.Marks[0].Extent);
        Assert.True(editor.Marks[0].Extent!.HalfWidth > 0);
    }

    [Fact]
    public void DraggingSizesTheMarkBeingDrawn()
    {
        var (editor, _, _) = Build();
        editor.SetDrawArmed(true);
        editor.TryBegin(100, 100);
        var before = editor.Marks[0].Extent!.HalfWidth;

        editor.Continue(160, 160);

        Assert.True(editor.Marks[0].Extent!.HalfWidth > before);
    }

    /// <summary>Sizing a mark is what finishes drawing it, so that is when it asks for its words.</summary>
    [Fact]
    public void FinishingTheDragAsksForTheWords()
    {
        var (editor, canvas, _) = Build();

        DrawOne(editor);

        Assert.True(canvas.Label.IsOpen);
        Assert.Equal(editor.Marks[0].Id, editor.EditingId);
    }

    [Fact]
    public void ADrawnMarkIsSaved()
    {
        var (editor, _, store) = Build();

        DrawOne(editor);

        Assert.Single(store.Saved["image.fits"]);
    }

    // ── Moving and resizing ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The grips belong to the SELECTED mark. They used to belong to the one being edited, which is
    /// only so while its label field is open — so a mark could be resized once and never again.
    /// </summary>
    [Fact]
    public void ASelectedMarkCanBeResizedByItsGrip()
    {
        var (editor, canvas, _) = Build();
        var mark = DrawOne(editor, 100, 100, 120, 120);
        canvas.Label.Commit("named");
        editor.Select(mark.Id);

        var before = editor.Marks[0].Extent!.HalfWidth;
        var box = AnnotationGeometry.HalfSize(editor.Marks[0], canvas.Surface, 3.0)!.Value;

        // A grip sits at a CORNER of the bounding box.
        Assert.True(editor.TryBegin(box.Cx + box.HalfW, box.Cy + box.HalfH));
        editor.Continue(box.Cx + box.HalfW + 40, box.Cy + box.HalfH + 40);
        editor.End();

        Assert.True(editor.Marks[0].Extent!.HalfWidth > before);
    }

    [Fact]
    public void PressingAMarksOutlineTakesHoldOfItAndMovesIt()
    {
        var (editor, canvas, _) = Build();
        var mark = DrawOne(editor);
        canvas.Label.Commit("named");
        editor.SetDrawArmed(false);

        var where = editor.Marks[0].Anchor;
        Assert.True(editor.TryBegin(where.X, where.Y));
        editor.Continue(where.X + 50, where.Y + 30);
        editor.End();

        Assert.NotEqual(where.X, editor.Marks[0].Anchor.X);
    }

    /// <summary>Taking hold of a mark picks it out, so its grips come alive without a second press.</summary>
    [Fact]
    public void TakingHoldOfAMarkSelectsIt()
    {
        var (editor, canvas, _) = Build();
        var mark = DrawOne(editor);
        canvas.Label.Commit("named");
        editor.Select(null);
        editor.SetDrawArmed(false);

        editor.TryBegin(editor.Marks[0].Anchor.X, editor.Marks[0].Anchor.Y);

        Assert.Equal(mark.Id, editor.SelectedId);
    }

    [Fact]
    public void AGestureThatNeverStartedIsNotOursToEnd()
        => Assert.False(Build().editor.End());

    // ── Naming ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CommittingWritesTheWordsOntoTheMark()
    {
        var (editor, canvas, _) = Build();
        DrawOne(editor);

        canvas.Label.Commit("NGC 4321");

        Assert.Equal("NGC 4321", editor.Marks[0].Text);
        Assert.False(canvas.Label.IsOpen);
        Assert.Null(editor.EditingId);
    }

    /// <summary>
    /// Escape leaves the mark exactly as it was — including a brand-new one. Drawing it was deliberate;
    /// not naming it yet is not a reason to lose it.
    /// </summary>
    [Fact]
    public void EscapeKeepsTheMarkAndItsWords()
    {
        var (editor, canvas, _) = Build();
        DrawOne(editor);
        canvas.Label.Commit("first");
        editor.BeginLabelEdit(editor.Marks[0].Id);

        canvas.Label.Escape();

        Assert.Single(editor.Marks);
        Assert.Equal("first", editor.Marks[0].Text);
    }

    /// <summary>The bin in the naming field is the way out of a mark drawn by accident.</summary>
    [Fact]
    public void TheBinRemovesTheMarkBeingNamed()
    {
        var (editor, canvas, _) = Build();
        DrawOne(editor);

        canvas.Label.Bin();

        Assert.Empty(editor.Marks);
        Assert.Null(editor.SelectedId);
    }

    [Fact]
    public void TheFieldOpensWithTheWordsAlreadyThere()
    {
        var (editor, canvas, _) = Build();
        DrawOne(editor);
        canvas.Label.Commit("first");

        editor.BeginLabelEdit(editor.Marks[0].Id);

        Assert.Equal("first", canvas.Label.Shown);
    }

    [Fact]
    public void ADoublePressOnAMarkRelabelsIt()
    {
        var (editor, canvas, _) = Build();
        var mark = DrawOne(editor);
        canvas.Label.Commit("named");

        Assert.True(editor.TryRelabelAt(mark.Anchor.X, mark.Anchor.Y));
        Assert.True(canvas.Label.IsOpen);
        Assert.Equal(mark.Id, editor.EditingId);
    }

    [Fact]
    public void ADoublePressOnEmptyCanvasRelabelsNothing()
        => Assert.False(Build().editor.TryRelabelAt(500, 500));

    // ── The pencil ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Putting the pencil down commits whatever was being named. Left open, a mark with no words yet is
    /// one that cannot be stored and quietly disappears on the next load.
    /// </summary>
    [Fact]
    public void PuttingThePencilDownClosesTheField()
    {
        var (editor, canvas, _) = Build();
        DrawOne(editor);

        editor.SetDrawArmed(false);

        Assert.False(canvas.Label.IsOpen);
        Assert.Null(editor.EditingId);
    }

    // ── The list ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Selecting a row is how a mark whose subject is off screen is found again.</summary>
    [Fact]
    public void SelectingAMarkGoesToIt()
    {
        var (editor, canvas, _) = Build();
        var mark = DrawOne(editor);
        canvas.Label.Commit("named");

        editor.Select(mark.Id);

        Assert.Equal(mark.Id, canvas.Revealed?.Id);
    }

    [Fact]
    public void DeletingAMarkRemovesItAndSaves()
    {
        var (editor, canvas, store) = Build();
        var mark = DrawOne(editor);
        canvas.Label.Commit("named");

        editor.Delete(mark.Id);

        Assert.Empty(editor.Marks);
        Assert.Empty(store.Saved["image.fits"]);
    }

    [Fact]
    public void ClearingRemovesEverything()
    {
        var (editor, canvas, _) = Build();
        DrawOne(editor, 100, 100);
        canvas.Label.Commit("a");
        DrawOne(editor, 300, 300);
        canvas.Label.Commit("b");

        editor.ClearAll();

        Assert.Empty(editor.Marks);
        Assert.Null(editor.SelectedId);
    }

    [Fact]
    public void EveryChangeIsAnnouncedSoTheListCanFollow()
    {
        var (editor, canvas, _) = Build();
        var heard = 0;
        editor.Changed += () => heard++;

        DrawOne(editor);
        canvas.Label.Commit("named");

        Assert.True(heard > 0);
    }

    /// <summary>A listener that throws must not take the drawing down with it.</summary>
    [Fact]
    public void AListenerThatThrowsDoesNotBreakDrawing()
    {
        var (editor, _, _) = Build();
        editor.Changed += () => throw new InvalidOperationException("boom");

        DrawOne(editor);

        Assert.Single(editor.Marks);
    }

    // ── Style ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>With a mark picked out, the controls restyle THAT mark.</summary>
    [Fact]
    public void StylingWithAMarkSelectedChangesThatMark()
    {
        var (editor, canvas, _) = Build();
        var mark = DrawOne(editor);
        canvas.Label.Commit("named");
        editor.Select(mark.Id);

        var red = MarkStyle.FromBytes(255, 0, 0, 14, bold: true, stroke: 3);
        editor.ApplyStyle(red);

        Assert.Equal(red, editor.Marks[0].Style);
    }

    /// <summary>With nothing selected, they say what the NEXT mark will look like.</summary>
    [Fact]
    public void StylingWithNothingSelectedSetsWhatComesNext()
    {
        var canvas = new Canvas();
        var preference = new InMemoryMarkStylePreference();
        var editor = new MarkEditor(canvas, new Store(), preference);

        var red = MarkStyle.FromBytes(255, 0, 0, 14, bold: true, stroke: 3);
        editor.ApplyStyle(red);

        Assert.Equal(red, preference.Default);
        Assert.Equal(red, DrawOne(editor).Style);
    }

    /// <summary>A style row on screen outranks the stored preference — it is what the person can see.</summary>
    [Fact]
    public void TheStyleRowOnScreenDecidesWhatTheNextMarkLooksLike()
    {
        var (editor, _, _) = Build();
        var green = MarkStyle.FromBytes(0, 255, 0, 20, bold: false, stroke: 5);
        editor.StyleSource = () => green;

        Assert.Equal(green, DrawOne(editor).Style);
    }

    [Fact]
    public void WithNothingOnScreenToAskTheStoredPreferenceDecides()
    {
        var preference = new InMemoryMarkStylePreference { Default = MarkStyle.AgentDefault };
        var editor = new MarkEditor(new Canvas(), new Store(), preference) { StyleSource = () => null };

        Assert.Equal(MarkStyle.AgentDefault, DrawOne(editor).Style);
    }

    // ── Following the file ──────────────────────────────────────────────────────────────────────

    /// <summary>A viewer that changes file must not keep drawing the last one's marks.</summary>
    [Fact]
    public void ChangingFileLoadsThatFilesMarks()
    {
        var (editor, canvas, store) = Build();
        DrawOne(editor);
        canvas.Label.Commit("first file");

        canvas.Target = "other.fits";
        editor.Render();

        Assert.Empty(editor.Marks);
        Assert.Null(editor.SelectedId);
    }

    /// <summary>An agent's change is re-read from the store, which is the truth.</summary>
    [Fact]
    public void RefreshRereadsTheStoreAndCanPickAMarkOut()
    {
        var (editor, _, store) = Build();
        store.Saved["image.fits"] =
        [
            new Annotation { Id = "agent1", Kind = AnnotationKind.Circle, Text = "found it",
                             Anchor = AnnotationAnchor.ImagePixel(5, 5), Extent = Extent.Square(3),
                             Author = MarkAuthor.Agent },
        ];

        Assert.True(editor.Refresh("image.fits", "agent1"));
        Assert.Equal("agent1", editor.SelectedId);
        Assert.Equal("found it", Assert.Single(editor.Marks).Text);
    }

    /// <summary>Another viewer's file is not this one's business.</summary>
    [Fact]
    public void RefreshForADifferentFileIsDeclined()
        => Assert.False(Build().editor.Refresh("somebody-elses.fits", null));

    // ── Drawing the canvas ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheCanvasIsToldWhatIsPickedOutAndWhatIsBeingNamed()
    {
        var (editor, canvas, _) = Build();
        var mark = DrawOne(editor);

        Assert.Equal(mark.Id, canvas.LastSelected);
        Assert.Equal(mark.Id, canvas.LastEditing);

        canvas.Label.Commit("named");
        Assert.Null(canvas.LastEditing);
        Assert.Equal(mark.Id, canvas.LastSelected);
    }
}
