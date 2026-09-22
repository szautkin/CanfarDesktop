using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// Which control an agent meant.
///
/// <para>The rule worth holding is that a wrong match is worse than no match. Pointing confidently at
/// one of two "Export" buttons shows somebody the wrong thing while telling them it is the right
/// thing — so an ambiguous name resolves to nothing and the caller is handed the candidates.</para>
/// </summary>
public class UiPointerTests
{
    private static UiPointer.Target T(string id, string? label = null, string kind = "Button")
        => new(id, kind, label);

    private static readonly UiPointer.Target[] Screen =
    [
        T("ExportButton", "Export figure…"),
        T("ClearButton", "Clear all marks"),
        T("DrawToggle", "Draw"),
        T("FilterBox", "Filter marks"),
        T("ColourButton", "Mark colour"),
    ];

    // ── Finding the one ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheNameOfTheControlFindsIt()
        => Assert.Equal("ExportButton", UiPointer.Best(Screen, "ExportButton"));

    /// <summary>An agent has usually been told the words a person can see, not the element's name.</summary>
    [Fact]
    public void TheWordsOnScreenFindItToo()
        => Assert.Equal("ClearButton", UiPointer.Best(Screen, "Clear all marks"));

    /// <summary>
    /// Case, spacing and a label's decorations are not part of what somebody meant — an ellipsis and
    /// a colon are typography.
    /// </summary>
    [Theory]
    [InlineData("exportbutton")]
    [InlineData("EXPORT BUTTON")]
    [InlineData("export_button")]
    [InlineData("  ExportButton  ")]
    public void PunctuationAndCasingDoNotMatter(string query)
        => Assert.Equal("ExportButton", UiPointer.Best(Screen, query));

    [Fact]
    public void AnEllipsisInTheLabelIsIgnored()
        => Assert.Equal("ExportButton", UiPointer.Best(Screen, "Export figure"));

    /// <summary>A partial name still lands when only one thing could be meant.</summary>
    [Fact]
    public void PartOfTheWordsIsEnoughWhenOnlyOneThingMatches()
        => Assert.Equal("FilterBox", UiPointer.Best(Screen, "filter"));

    // ── Refusing to guess ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Two equally good matches is a question, not a coin toss. Pointing at one of two Export buttons
    /// shows somebody the wrong control while telling them it is the right one.
    /// </summary>
    [Fact]
    public void TwoEqualMatchesResolveToNothing()
    {
        var ambiguous = new[] { T("ExportA", "Export"), T("ExportB", "Export") };

        Assert.Null(UiPointer.Best(ambiguous, "Export"));
    }

    /// <summary>
    /// A stronger tier wins outright. An exact id beats a label that merely contains the word, so
    /// adding a vaguely-named control elsewhere cannot steal an exact match.
    /// </summary>
    [Fact]
    public void AnExactIdBeatsAPartialLabel()
    {
        var targets = new[] { T("Export", "Save"), T("Other", "Export this figure") };

        Assert.Equal("Export", UiPointer.Best(targets, "Export"));
    }

    /// <summary>And an exact label beats a partial one for the same reason.</summary>
    [Fact]
    public void AnExactLabelBeatsAPartialOne()
    {
        var targets = new[] { T("A", "Draw"), T("B", "Draw marks on the image") };

        Assert.Equal("A", UiPointer.Best(targets, "Draw"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("something that is not here")]
    public void NothingSensibleMatchesNothing(string? query)
        => Assert.Null(UiPointer.Best(Screen, query));

    [Fact]
    public void AnEmptyScreenMatchesNothing()
        => Assert.Null(UiPointer.Best([], "ExportButton"));

    // ── What to offer instead ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// A miss always comes back with candidates. An agent that guessed a name can then choose from
    /// what is actually there instead of guessing again.
    /// </summary>
    [Fact]
    public void AMissStillOffersTheNearestThings()
    {
        var suggestions = UiPointer.Suggest(Screen, "export");

        Assert.NotEmpty(suggestions);
        Assert.Equal("ExportButton", suggestions[0].Id);
    }

    /// <summary>With nothing asked for, it is simply what is on screen.</summary>
    [Fact]
    public void NoQueryListsWhatIsThere()
        => Assert.Equal(Screen.Length, UiPointer.Suggest(Screen, null).Count);

    [Fact]
    public void TheListIsCapped()
        => Assert.Equal(2, UiPointer.Suggest(Screen, null, max: 2).Count);

    [Fact]
    public void AskingForNoneGivesNone()
        => Assert.Empty(UiPointer.Suggest(Screen, "export", max: 0));

    /// <summary>Ambiguity is the case that most needs the candidates, so it must still produce them.</summary>
    [Fact]
    public void AnAmbiguousNameStillOffersBothCandidates()
    {
        var ambiguous = new[] { T("ExportA", "Export"), T("ExportB", "Export") };

        Assert.Equal(2, UiPointer.Suggest(ambiguous, "Export").Count);
    }

    // ── How long it stays ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void NoDurationAskedGetsTheDefault()
        => Assert.Equal(UiPointer.DefaultSeconds, UiPointer.Seconds(null));

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(100000.0)]
    public void AnUnusableDurationBecomesAUsableOne(double asked)
    {
        var seconds = UiPointer.Seconds(asked);

        Assert.InRange(seconds, UiPointer.MinSeconds, UiPointer.MaxSeconds);
    }

    [Fact]
    public void AReasonableDurationIsKept()
        => Assert.Equal(12.0, UiPointer.Seconds(12.0));
}
