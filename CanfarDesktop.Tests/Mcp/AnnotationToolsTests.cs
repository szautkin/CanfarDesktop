using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Mcp.Wire;
using CanfarDesktop.Models;
using CanfarDesktop.Services.Fits;

namespace CanfarDesktop.Tests.Mcp;

/// <summary>
/// The annotation tools. The store is a real one on a temp path — the marks going to disk and coming
/// back is half of what these tools do — and the viewer is a fake, because none of them needs a window.
/// </summary>
public class AnnotationToolsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "verbinal-anntools-" + Guid.NewGuid().ToString("N"));
    private readonly FakeHost _host = new();

    private AnnotationStore Store() => new(Path.Combine(_dir, "annotations.json"));

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private sealed class FakeHost : IAnnotationHost
    {
        public string? FitsTarget { get; set; }
        public string? CubeTarget { get; set; }
        public bool Refreshed { get; private set; }
        public string? LastSelected { get; private set; }

        /// <summary>Set when the viewer was told to stop pointing at anything.</summary>
        public bool Deselected { get; private set; }

        public Task<string?> ActiveTargetAsync(AnnotationViewer viewer)
            => Task.FromResult(viewer == AnnotationViewer.Cube ? CubeTarget : FitsTarget);

        public Task<bool> RefreshAsync(AnnotationViewer viewer, string target, string? selectId)
        {
            Refreshed = true;
            LastSelected = selectId;
            var open = viewer == AnnotationViewer.Cube ? CubeTarget : FitsTarget;
            return Task.FromResult(string.Equals(open, target, StringComparison.OrdinalIgnoreCase));
        }

        public Task<bool> DeselectAsync(AnnotationViewer viewer, string target)
        {
            Deselected = true;
            LastSelected = null;
            var open = viewer == AnnotationViewer.Cube ? CubeTarget : FitsTarget;
            return Task.FromResult(string.Equals(open, target, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static JsonValue Args(string json) => JsonValue.Parse(json);
    private static McpToolContext Ctx() => McpToolContext.ForExternal("c1", Guid.NewGuid());

    private static T Payload<T>(ToolResult result)
        => JsonSerializer.Deserialize<T>(Assert.IsType<DataResult>(result).Json, McpJson.Options)!;

    private static string Failure(ToolResult result) => Assert.IsType<FailedResult>(result).Reason.Description;

    // ── annotate_fits ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AMarkIsDrawnOnTheImageOnScreenAndKeptWithIt()
    {
        _host.FitsTarget = "C:/data/m31.fits";
        var store = Store();

        var change = Payload<AnnotationChange>(await new AnnotateFitsTool(store, _host)
            .InvokeAsync(Args("""{"x":100,"y":200,"radius":15,"text":"the nucleus"}"""), Ctx(), default));

        Assert.True(change.Applied);
        Assert.True(change.Shown);
        Assert.Equal("C:/data/m31.fits", change.Target);
        Assert.Equal("circle", change.Annotation!.Kind);
        Assert.Equal("imagePixel", change.Annotation.Space);
        Assert.Equal(15, change.Annotation.HalfWidth);

        // And it is on disk against the file, not in the window.
        Assert.Single(store.LoadFor("C:/data/m31.fits"));
    }

    /// <summary>An agent's marks say so — the panel shows it and the ink differs.</summary>
    [Fact]
    public async Task AMarkDrawnByAnAgentSaysSo()
    {
        _host.FitsTarget = "a.fits";

        var change = Payload<AnnotationChange>(await new AnnotateFitsTool(Store(), _host)
            .InvokeAsync(Args("""{"x":1,"y":2}"""), Ctx(), default));

        Assert.Equal("agent", change.Annotation!.Author);
        Assert.Equal(MarkStyle.AgentDefault.ColourHex(), change.Annotation.Colour);
    }

    [Fact]
    public async Task ASkyPositionIsKeptAsOne()
    {
        _host.FitsTarget = "a.fits";

        var change = Payload<AnnotationChange>(await new AnnotateFitsTool(Store(), _host)
            .InvokeAsync(Args("""{"raDeg":10.6847,"decDeg":41.2687,"radius":0.01}"""), Ctx(), default));

        Assert.Equal("sky", change.Annotation!.Space);
        Assert.Equal(10.6847, change.Annotation.X, 6);
    }

    [Fact]
    public async Task APositionGivenTwiceIsRefusedRatherThanOneOfThemPicked()
    {
        _host.FitsTarget = "a.fits";

        var message = Failure(await new AnnotateFitsTool(Store(), _host)
            .InvokeAsync(Args("""{"x":1,"y":2,"raDeg":10,"decDeg":41}"""), Ctx(), default));

        Assert.Contains("once", message);
    }

    [Fact]
    public async Task AMarkWithNoPositionIsRefusedAndToldWhatToGive()
    {
        _host.FitsTarget = "a.fits";

        var message = Failure(await new AnnotateFitsTool(Store(), _host).InvokeAsync(Args("{}"), Ctx(), default));

        Assert.Contains("needs a position", message);
        Assert.Contains("raDeg", message);
    }

    /// <summary>Nothing open is an answer with a way forward, not a failure.</summary>
    [Fact]
    public async Task WithNoImageOpenItSaysSoAndOffersTheWayRound()
    {
        var change = Payload<AnnotationChange>(await new AnnotateFitsTool(Store(), _host)
            .InvokeAsync(Args("""{"x":1,"y":2}"""), Ctx(), default));

        Assert.False(change.Applied);
        Assert.Contains("no FITS file is open", change.Message);
        Assert.Contains("target", change.Message);
    }

    /// <summary>
    /// A file that is not on screen can still be annotated — that is how a batch of marks gets prepared
    /// before anyone looks at it. The reply says the viewer is not showing it rather than pretending.
    /// </summary>
    [Fact]
    public async Task AFileThatIsNotOnScreenCanStillBeMarked()
    {
        _host.FitsTarget = "open.fits";
        var store = Store();

        var change = Payload<AnnotationChange>(await new AnnotateFitsTool(store, _host)
            .InvokeAsync(Args("""{"x":1,"y":2,"target":"other.fits"}"""), Ctx(), default));

        Assert.True(change.Applied);
        Assert.False(change.Shown);
        Assert.Contains("not showing it", change.Message);
        Assert.Single(store.LoadFor("other.fits"));
        Assert.Empty(store.LoadFor("open.fits"));
    }

    [Fact]
    public async Task ACalloutWithNothingToSayIsRefused()
    {
        _host.FitsTarget = "a.fits";

        var message = Failure(await new AnnotateFitsTool(Store(), _host)
            .InvokeAsync(Args("""{"x":1,"y":2,"kind":"callout"}"""), Ctx(), default));

        Assert.Contains("needs text", message);
    }

    [Fact]
    public async Task AColourThatIsNotOneIsRefusedRatherThanDrawnBlack()
    {
        _host.FitsTarget = "a.fits";

        var message = Failure(await new AnnotateFitsTool(Store(), _host)
            .InvokeAsync(Args("""{"x":1,"y":2,"colour":"reddish"}"""), Ctx(), default));

        Assert.Contains("is not a colour", message);
        Assert.Contains("#rrggbb", message);
    }

    // ── annotate_cube ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ACubeMarkLivesOnAChannel()
    {
        _host.CubeTarget = "cube.fits";

        var change = Payload<AnnotationChange>(await new AnnotateCubeTool(Store(), _host)
            .InvokeAsync(Args("""{"x":32,"y":48,"channel":17,"radius":6}"""), Ctx(), default));

        Assert.True(change.Applied);
        Assert.Equal("data", change.Annotation!.Space);
        Assert.Equal(17, change.Annotation.Z);
    }

    // ── Listing ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListingAnswersTheIdsTheOtherToolsTake()
    {
        _host.FitsTarget = "a.fits";
        var store = Store();
        await new AnnotateFitsTool(store, _host).InvokeAsync(Args("""{"x":1,"y":2,"text":"one"}"""), Ctx(), default);

        var listed = Payload<AnnotationListView>(await new ListFitsAnnotationsTool(store, _host)
            .InvokeAsync(Args("{}"), Ctx(), default));

        Assert.Equal(1, listed.Count);
        Assert.True(listed.Shown);
        Assert.NotEmpty(listed.Annotations[0].Id);
        Assert.Equal("one", listed.Annotations[0].Text);
    }

    [Fact]
    public async Task AFileWithNoMarksSaysSoRatherThanAnsweringNothing()
    {
        _host.FitsTarget = "bare.fits";

        var listed = Payload<AnnotationListView>(await new ListFitsAnnotationsTool(Store(), _host)
            .InvokeAsync(Args("{}"), Ctx(), default));

        Assert.Equal(0, listed.Count);
        Assert.Contains("nothing has been drawn", listed.Message);
    }

    [Fact]
    public void ListingIsAReadAndTheRestChangeSomething()
    {
        var store = Store();
        Assert.Equal(McpVerbClass.Read, new ListFitsAnnotationsTool(store, _host).VerbClass);
        Assert.Equal(McpVerbClass.Read, new ListCubeAnnotationsTool(store, _host).VerbClass);
        Assert.Equal(McpVerbClass.ViewState, new AnnotateFitsTool(store, _host).VerbClass);
        Assert.Equal(McpVerbClass.ViewState, new UpdateAnnotationTool(store, _host).VerbClass);
        Assert.Equal(McpVerbClass.ViewState, new SelectAnnotationTool(store, _host).VerbClass);
        // Removing a drawing is not something an undo brings back from here.
        Assert.Equal(McpVerbClass.Destructive, new RemoveAnnotationTool(store, _host).VerbClass);
    }

    // ── update_annotation ───────────────────────────────────────────────────────────────────────

    private async Task<(AnnotationStore Store, string Id)> OneMark(string target = "a.fits")
    {
        _host.FitsTarget = target;
        var store = Store();
        var change = Payload<AnnotationChange>(await new AnnotateFitsTool(store, _host)
            .InvokeAsync(Args("""{"x":100,"y":100,"radius":10,"text":"before"}"""), Ctx(), default));
        return (store, change.Annotation!.Id);
    }

    [Fact]
    public async Task AMarkCanBeMovedAndRelabelled()
    {
        var (store, id) = await OneMark();

        var change = Payload<AnnotationChange>(await new UpdateAnnotationTool(store, _host)
            .InvokeAsync(Args($$"""{"id":"{{id}}","x":150,"text":"after"}"""), Ctx(), default));

        Assert.True(change.Applied);
        Assert.Equal(150, change.Annotation!.X);
        Assert.Equal(100, change.Annotation.Y);   // untouched
        Assert.Equal("after", change.Annotation.Text);
    }

    /// <summary>
    /// A caller changing only the colour must not have the weight reset under it — the style is applied
    /// over the mark's CURRENT appearance, not over a default.
    /// </summary>
    [Fact]
    public async Task ChangingOneStyleFieldLeavesTheOthers()
    {
        var (store, id) = await OneMark();

        await new UpdateAnnotationTool(store, _host)
            .InvokeAsync(Args($$"""{"id":"{{id}}","stroke":4,"bold":true}"""), Ctx(), default);

        var change = Payload<AnnotationChange>(await new UpdateAnnotationTool(store, _host)
            .InvokeAsync(Args($$"""{"id":"{{id}}","colour":"#ff0000"}"""), Ctx(), default));

        Assert.Equal("#ff0000", change.Annotation!.Colour);
        Assert.Equal(4, change.Annotation.Stroke);
        Assert.True(change.Annotation.Bold);
    }

    [Fact]
    public async Task AnUnknownIdIsNamedWithWhereToLook()
    {
        var (store, _) = await OneMark();

        var message = Failure(await new UpdateAnnotationTool(store, _host)
            .InvokeAsync(Args("""{"id":"nope","x":1}"""), Ctx(), default));

        Assert.Contains("no mark 'nope'", message);
        Assert.Contains("list_fits_annotations", message);
    }

    [Fact]
    public async Task AChangeThatWouldMakeTheMarkUndrawableIsRefused()
    {
        var (store, id) = await OneMark();

        var message = Failure(await new UpdateAnnotationTool(store, _host)
            .InvokeAsync(Args($$"""{"id":"{{id}}","radius":0}"""), Ctx(), default));

        Assert.Contains("greater than zero", message);
        // And the mark it would have broken is untouched.
        Assert.Equal(10, store.LoadFor("a.fits")[0].Extent!.HalfWidth);
    }

    [Fact]
    public async Task AnUnknownViewerIsRefusedByName()
    {
        var (store, id) = await OneMark();

        var message = Failure(await new UpdateAnnotationTool(store, _host)
            .InvokeAsync(Args($$"""{"id":"{{id}}","viewer":"spectrum"}"""), Ctx(), default));

        Assert.Contains("'fits' or 'cube'", message);
    }

    // ── remove_annotation ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RemovingTakesTheMarkOffAndSaysWhatIsLeft()
    {
        var (store, id) = await OneMark();

        var change = Payload<AnnotationChange>(await new RemoveAnnotationTool(store, _host)
            .InvokeAsync(Args($$"""{"id":"{{id}}"}"""), Ctx(), default));

        Assert.True(change.Applied);
        Assert.Equal(0, change.Remaining);
        Assert.Empty(store.LoadFor("a.fits"));
    }

    [Fact]
    public async Task RemovingSomethingThatIsNotThereIsNamedRatherThanReportedAsDone()
    {
        var (store, _) = await OneMark();

        var message = Failure(await new RemoveAnnotationTool(store, _host)
            .InvokeAsync(Args("""{"id":"ghost"}"""), Ctx(), default));

        Assert.Contains("no mark 'ghost'", message);
    }

    // ── select_annotation ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SelectingPointsAtTheMarkWithoutChangingIt()
    {
        var (store, id) = await OneMark();
        var before = store.LoadFor("a.fits")[0];

        var change = Payload<AnnotationChange>(await new SelectAnnotationTool(store, _host)
            .InvokeAsync(Args($$"""{"id":"{{id}}"}"""), Ctx(), default));

        Assert.True(change.Applied);
        Assert.Equal(id, _host.LastSelected);
        Assert.Equal(before, store.LoadFor("a.fits")[0]);
    }

    [Fact]
    public async Task SelectingAMarkThatIsNotOnTheOpenFileIsNamed()
    {
        var (store, id) = await OneMark();
        _host.FitsTarget = "different.fits";

        var message = Failure(await new SelectAnnotationTool(store, _host)
            .InvokeAsync(Args($$"""{"id":"{{id}}"}"""), Ctx(), default));

        Assert.Contains("current file", message);
    }

    /// <summary>
    /// Omitting the id stops pointing — the tool's half of the gesture a person makes by clicking the
    /// mark again. An agent that picked something out could previously only move the selection to
    /// another mark, never put it down.
    /// </summary>
    [Fact]
    public async Task SelectingWithNoIdLetsGoOfWhateverWasPickedOut()
    {
        var (store, id) = await OneMark();

        await new SelectAnnotationTool(store, _host).InvokeAsync(Args($$"""{"id":"{{id}}"}"""), Ctx(), default);
        Assert.Equal(id, _host.LastSelected);

        var change = Payload<AnnotationChange>(await new SelectAnnotationTool(store, _host)
            .InvokeAsync(Args("{}"), Ctx(), default));

        Assert.True(change.Applied);
        Assert.True(_host.Deselected);
        Assert.Null(_host.LastSelected);
        Assert.Null(change.Annotation);
    }

    /// <summary>Letting go changes nothing about the marks themselves.</summary>
    [Fact]
    public async Task LettingGoLeavesEveryMarkExactlyAsItWas()
    {
        var (store, _) = await OneMark();
        var before = store.LoadFor("a.fits")[0];

        var change = Payload<AnnotationChange>(await new SelectAnnotationTool(store, _host)
            .InvokeAsync(Args("{}"), Ctx(), default));

        Assert.Equal(before, store.LoadFor("a.fits")[0]);
        Assert.Equal(1, change.Remaining);
    }

    /// <summary>With nothing open there is nothing to stop pointing at, and that is said rather than thrown.</summary>
    [Fact]
    public async Task LettingGoWithNothingOpenIsReported()
    {
        var (store, _) = await OneMark();
        _host.FitsTarget = null;

        var change = Payload<AnnotationChange>(await new SelectAnnotationTool(store, _host)
            .InvokeAsync(Args("{}"), Ctx(), default));

        Assert.False(change.Applied);
        Assert.Contains("nothing is open", change.Message);
    }

    // ── clear_annotations ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Removing a dozen marks one id at a time worked, and cost a listing plus a call per mark. The
    /// count it answers with is what was actually taken off.
    /// </summary>
    [Fact]
    public async Task ClearTakesEveryMarkOffTheFileOnScreen()
    {
        _host.FitsTarget = "C:/data/m31.fits";
        var store = Store();

        await new AnnotateFitsTool(store, _host).InvokeAsync(Args("""{"x":1,"y":2,"radius":5}"""), Ctx(), default);
        await new AnnotateFitsTool(store, _host).InvokeAsync(Args("""{"x":3,"y":4,"radius":5}"""), Ctx(), default);
        Assert.Equal(2, store.LoadFor("C:/data/m31.fits").Count);

        var change = Payload<AnnotationChange>(await new ClearAnnotationsTool(store, _host)
            .InvokeAsync(Args("{}"), Ctx(), default));

        Assert.True(change.Applied);
        Assert.Equal(2, change.Removed);
        Assert.Equal(0, change.Remaining);
        Assert.Empty(store.LoadFor("C:/data/m31.fits"));
    }

    /// <summary>Clearing a file with nothing on it reports zero rather than claiming work it did not do.</summary>
    [Fact]
    public async Task ClearOnAnUnmarkedFileRemovesNothingAndSaysSo()
    {
        _host.FitsTarget = "C:/data/m31.fits";

        var change = Payload<AnnotationChange>(await new ClearAnnotationsTool(Store(), _host)
            .InvokeAsync(Args("{}"), Ctx(), default));

        Assert.True(change.Applied);
        Assert.Equal(0, change.Removed);
    }

    /// <summary>One file's marks, not every file's — the store is keyed by target for a reason.</summary>
    [Fact]
    public async Task ClearLeavesOtherFilesAlone()
    {
        var store = Store();

        _host.FitsTarget = "C:/data/a.fits";
        await new AnnotateFitsTool(store, _host).InvokeAsync(Args("""{"x":1,"y":2,"radius":5}"""), Ctx(), default);

        _host.FitsTarget = "C:/data/b.fits";
        await new AnnotateFitsTool(store, _host).InvokeAsync(Args("""{"x":1,"y":2,"radius":5}"""), Ctx(), default);

        await new ClearAnnotationsTool(store, _host).InvokeAsync(Args("{}"), Ctx(), default);

        Assert.Empty(store.LoadFor("C:/data/b.fits"));
        Assert.Single(store.LoadFor("C:/data/a.fits"));
    }

    /// <summary>Nothing open and no target named is a question with no subject.</summary>
    [Fact]
    public async Task ClearWithNothingOpenSaysWhatToDoInstead()
    {
        _host.FitsTarget = null;

        var change = Payload<AnnotationChange>(await new ClearAnnotationsTool(Store(), _host)
            .InvokeAsync(Args("{}"), Ctx(), default));

        Assert.False(change.Applied);
        Assert.Contains("target", change.Message);
    }

    /// <summary>It is destructive, and gated as such — there is no undo behind it.</summary>
    [Fact]
    public void ClearIsDestructive()
        => Assert.Equal(McpVerbClass.Destructive, new ClearAnnotationsTool(Store(), _host).VerbClass);

    // ── Marks per extension ─────────────────────────────────────────────────────────────────────
    //
    // A multi-extension file is several images. The viewer's key names the extension on screen, and
    // a mark drawn on one chip must not turn up on another.

    private const string Mef = "C:/data/mef.fits";
    private static readonly string OnChip2 = CanfarDesktop.Helpers.MarkTarget.Key(Mef, 2);
    private static readonly string OnChip5 = CanfarDesktop.Helpers.MarkTarget.Key(Mef, 5);

    private static Annotation Pixel(string id) => new()
    {
        Id = id, Kind = AnnotationKind.Circle, Anchor = AnnotationAnchor.ImagePixel(10, 20),
        Extent = Extent.Square(5), CreatedAt = "2026-09-23T00:00:00Z",
    };

    [Fact]
    public async Task AMarkLandsOnTheExtensionOnScreen()
    {
        _host.FitsTarget = OnChip2;
        var store = Store();

        await new AnnotateFitsTool(store, _host).InvokeAsync(Args("""{"x":1,"y":2}"""), Ctx(), default);

        Assert.Single(store.LoadFor(OnChip2));
        Assert.Empty(store.LoadFor(OnChip5));
    }

    /// <summary>"This file" means the chip the person is looking at, not the file's first one.</summary>
    [Fact]
    public async Task NamingTheFileOnScreenMeansItsExtensionOnScreen()
    {
        _host.FitsTarget = OnChip2;
        var store = Store();

        await new AnnotateFitsTool(store, _host).InvokeAsync(
            Args($$"""{"x":1,"y":2,"target":"{{Mef}}"}"""), Ctx(), default);

        Assert.Single(store.LoadFor(OnChip2));
    }

    [Fact]
    public async Task AnExplicitExtensionIsHonoured()
    {
        _host.FitsTarget = OnChip2;
        var store = Store();

        await new AnnotateFitsTool(store, _host).InvokeAsync(Args("""{"x":1,"y":2,"hdu":5}"""), Ctx(), default);

        Assert.Single(store.LoadFor(OnChip5));
        Assert.Empty(store.LoadFor(OnChip2));
    }

    [Fact]
    public async Task TheListShowsTheExtensionOnScreenByDefault()
    {
        _host.FitsTarget = OnChip2;
        var store = Store();
        store.Add(OnChip2, Pixel("a"));
        store.Add(OnChip5, Pixel("b"));

        var list = Payload<AnnotationListView>(await new ListFitsAnnotationsTool(store, _host)
            .InvokeAsync(Args("{}"), Ctx(), default));

        Assert.Equal(["a"], list.Annotations.Select(m => m.Id));
    }

    /// <summary>Across extensions, every mark says which chip it is on.</summary>
    [Fact]
    public async Task EveryExtensionCanBeListedAndEachMarkSaysWhichItIsOn()
    {
        _host.FitsTarget = OnChip2;
        var store = Store();
        store.Add(OnChip2, Pixel("a"));
        store.Add(OnChip5, Pixel("b"));
        store.Add("C:/data/other.fits#2", Pixel("elsewhere"));

        var list = Payload<AnnotationListView>(await new ListFitsAnnotationsTool(store, _host)
            .InvokeAsync(Args("""{"allHdus":true}"""), Ctx(), default));

        Assert.Equal(2, list.Count);
        Assert.Equal(2, list.Annotations.Single(m => m.Id == "a").Hdu);
        Assert.Equal(5, list.Annotations.Single(m => m.Id == "b").Hdu);
    }

    /// <summary>
    /// Ids are unique across a file, so an agent can change a mark by id after the person has moved
    /// to another chip, without knowing which extension it went on.
    /// </summary>
    [Fact]
    public async Task AMarkOnAnotherExtensionCanStillBeChangedByItsId()
    {
        _host.FitsTarget = OnChip2;
        var store = Store();
        store.Add(OnChip5, Pixel("b"));

        var change = Payload<AnnotationChange>(await new UpdateAnnotationTool(store, _host)
            .InvokeAsync(Args("""{"id":"b","text":"found it"}"""), Ctx(), default));

        Assert.True(change.Applied);
        Assert.Equal("found it", store.LoadFor(OnChip5).Single().Text);
    }

    [Fact]
    public async Task AMarkOnAnotherExtensionCanStillBeRemovedByItsId()
    {
        _host.FitsTarget = OnChip2;
        var store = Store();
        store.Add(OnChip5, Pixel("b"));

        await new RemoveAnnotationTool(store, _host).InvokeAsync(Args("""{"id":"b"}"""), Ctx(), default);

        Assert.Empty(store.LoadFor(OnChip5));
    }

    /// <summary>
    /// Picking out a mark on a chip that is not showing would highlight nothing, so it says where the
    /// mark is and how to get there instead of claiming it is shown.
    /// </summary>
    [Fact]
    public async Task SelectingAMarkOnAnotherExtensionSaysWhereItIs()
    {
        _host.FitsTarget = OnChip2;
        var store = Store();
        store.Add(OnChip5, Pixel("b"));

        var change = Payload<AnnotationChange>(await new SelectAnnotationTool(store, _host)
            .InvokeAsync(Args("""{"id":"b"}"""), Ctx(), default));

        Assert.False(change.Shown);
        Assert.Contains("extension 5", change.Message);
    }

    /// <summary>Clearing is the tool with no undo, so by default it takes only the image on screen.</summary>
    [Fact]
    public async Task ClearingTakesOnlyTheExtensionOnScreenByDefault()
    {
        _host.FitsTarget = OnChip2;
        var store = Store();
        store.Add(OnChip2, Pixel("a"));
        store.Add(OnChip5, Pixel("b"));

        var change = Payload<AnnotationChange>(await new ClearAnnotationsTool(store, _host)
            .InvokeAsync(Args("{}"), Ctx(), default));

        Assert.Equal(1, change.Removed);
        Assert.Empty(store.LoadFor(OnChip2));
        Assert.Single(store.LoadFor(OnChip5));
    }

    [Fact]
    public async Task ClearingEveryExtensionHasToBeAskedFor()
    {
        _host.FitsTarget = OnChip2;
        var store = Store();
        store.Add(OnChip2, Pixel("a"));
        store.Add(OnChip5, Pixel("b"));
        store.Add("C:/data/other.fits#2", Pixel("kept"));

        var change = Payload<AnnotationChange>(await new ClearAnnotationsTool(store, _host)
            .InvokeAsync(Args("""{"allHdus":true}"""), Ctx(), default));

        Assert.Equal(2, change.Removed);
        Assert.Empty(store.LoadFor(OnChip5));
        Assert.Single(store.LoadFor("C:/data/other.fits#2"));
    }
}
