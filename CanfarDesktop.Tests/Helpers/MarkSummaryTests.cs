using Xunit;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// The rows of the Marks list.
///
/// The list exists to answer two questions a canvas cannot: what have I marked, and where is the one
/// whose subject is off screen. A row that drops the place, or that reads the same for a mark of yours
/// and a mark an agent made, fails at exactly that.
/// </summary>
public class MarkSummaryTests
{
    private static Annotation Mark(
        string id = "m1",
        string text = "",
        AnnotationKind kind = AnnotationKind.Circle,
        MarkAuthor author = MarkAuthor.User,
        AnnotationAnchor? anchor = null) => new()
    {
        Id = id,
        Text = text,
        Kind = kind,
        Author = author,
        Anchor = anchor ?? AnnotationAnchor.ImagePixel(512, 384),
    };

    // ── The headline ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AMarkWithWordsIsNamedByThem()
        => Assert.Equal("NGC 4321 core", MarkSummary.Title(Mark(text: "NGC 4321 core")));

    /// <summary>
    /// A mark with no label is still a mark somebody drew and still has to be clickable. An empty row
    /// is one you cannot pick on purpose.
    /// </summary>
    [Fact]
    public void AMarkWithNoWordsIsNamedByItsShape()
        => Assert.Equal("(circle)", MarkSummary.Title(Mark()));

    [Fact]
    public void WhitespaceIsNotALabel()
        => Assert.Equal("(box)", MarkSummary.Title(Mark(text: "   ", kind: AnnotationKind.Rect)));

    [Fact]
    public void ALabelIsTrimmedSoTheListStaysAligned()
        => Assert.Equal("bright knot", MarkSummary.Title(Mark(text: "  bright knot  ")));

    // ── Where it is ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// "512, 384" is three different places depending on the space. Naming it is the whole reason a
    /// mark carries one.
    /// </summary>
    [Fact]
    public void APixelAnchorSaysPixel()
        => Assert.Equal("pixel 512, 384", MarkSummary.Place(AnnotationAnchor.ImagePixel(512.4, 383.8)));

    [Fact]
    public void ASkyAnchorSaysDegreesAtFourPlaces()
        => Assert.Equal("185.7285°, 15.8223°", MarkSummary.Place(AnnotationAnchor.Sky(185.72853, 15.82231)));

    /// <summary>A cube mark lives on a channel, and the channel is the half that says which slice.</summary>
    [Fact]
    public void ADataAnchorSaysItsChannel()
        => Assert.Equal("voxel 128, 96, ch 42", MarkSummary.Place(AnnotationAnchor.Data(128, 96, 42)));

    // ── The second line ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheDetailSaysWhatItIsAndWhereItIs()
        => Assert.Equal("circle — pixel 512, 384", MarkSummary.Describe(Mark()));

    /// <summary>
    /// An agent's marks and a person's sit in the same list, and deleting someone else's work by
    /// mistake is the thing to prevent.
    /// </summary>
    [Fact]
    public void AnAgentsMarkSaysSo()
        => Assert.Equal("circle — pixel 512, 384 — by the agent",
            MarkSummary.Describe(Mark(author: MarkAuthor.Agent)));

    /// <summary>A list where every row ends "by you" says nothing; a person's marks are the plain case.</summary>
    [Fact]
    public void APersonsMarkIsNotAnnouncedAsTheirs()
        => Assert.DoesNotContain("by", MarkSummary.Describe(Mark()));

    [Theory]
    [InlineData(AnnotationKind.Circle, "circle")]
    [InlineData(AnnotationKind.Rect, "box")]
    [InlineData(AnnotationKind.Callout, "callout")]
    [InlineData(AnnotationKind.Text, "text")]
    public void EveryKindHasAWord(AnnotationKind kind, string expected)
        => Assert.Equal(expected, MarkSummary.KindLabel(kind));

    // ── The list ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EveryMarkGetsARowAndKeepsItsId()
    {
        var lines = MarkSummary.Lines([Mark("a"), Mark("b"), Mark("c")]);

        Assert.Equal(["a", "b", "c"], lines.Select(l => l.Id));
    }

    [Fact]
    public void TheRowRemembersWhetherAnAgentDrewIt()
    {
        var lines = MarkSummary.Lines([Mark("a"), Mark("b", author: MarkAuthor.Agent)]);

        Assert.False(lines[0].ByAgent);
        Assert.True(lines[1].ByAgent);
    }

    [Fact]
    public void AnEmptyFilterKeepsEverything()
        => Assert.Equal(2, MarkSummary.Lines([Mark("a"), Mark("b")], "   ").Count);

    [Fact]
    public void TheFilterMatchesTheWords()
    {
        var lines = MarkSummary.Lines([Mark("a", text: "NGC 4321"), Mark("b", text: "foreground star")], "ngc");

        Assert.Equal("a", Assert.Single(lines).Id);
    }

    /// <summary>
    /// Somebody looking through thirty marks is as likely to remember where one was as what they
    /// called it, so the place is searchable too.
    /// </summary>
    [Fact]
    public void TheFilterAlsoMatchesThePlace()
    {
        var lines = MarkSummary.Lines(
            [Mark("a", anchor: AnnotationAnchor.ImagePixel(512, 384)),
             Mark("b", anchor: AnnotationAnchor.ImagePixel(20, 30))], "512");

        Assert.Equal("a", Assert.Single(lines).Id);
    }

    [Fact]
    public void TheFilterIgnoresCase()
        => Assert.Single(MarkSummary.Lines([Mark("a", text: "Bright Knot")], "BRIGHT knot"));

    [Fact]
    public void AFilterThatMatchesNothingGivesAnEmptyList()
        => Assert.Empty(MarkSummary.Lines([Mark("a", text: "NGC 4321")], "quasar"));

    // ── The count ───────────────────────────────────────────────────────────────────────────────

    /// <summary>A permanent "0 marks" is furniture; a count that appears is information.</summary>
    [Fact]
    public void NoMarksHaveNoCount()
        => Assert.Null(MarkSummary.Count(0));

    [Fact]
    public void MarksAreCounted()
        => Assert.Equal("3 marks", MarkSummary.Count(3));

    // ── Translation ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The hook exists because the resource loader needs a packaged app and this has to be testable
    /// without one. Unset, it must answer in English rather than in resource keys.
    /// </summary>
    [Fact]
    public void TranslationsGoThroughTheHookAndFallBackToEnglish()
    {
        var previous = MarkSummary.Translate;
        try
        {
            MarkSummary.Translate = key => key == "Mark_KindCircle" ? "cercle" : null;

            Assert.Equal("(cercle)", MarkSummary.Title(Mark()));
            Assert.Equal("(box)", MarkSummary.Title(Mark(kind: AnnotationKind.Rect)));   // no French, English stands
        }
        finally
        {
            MarkSummary.Translate = previous;
        }
    }
}
