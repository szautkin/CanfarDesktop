using Xunit;
using CanfarDesktop.Models;

namespace CanfarDesktop.Tests.Models;

/// <summary>The mark model: what can be drawn, what cannot, and what a colour survives as.</summary>
public class AnnotationTests
{
    private static Annotation Valid() => new()
    {
        Id = "m1",
        Kind = AnnotationKind.Circle,
        Anchor = AnnotationAnchor.ImagePixel(10, 20),
        Extent = Extent.Square(5),
    };

    // ── Anchors ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A NaN reaches the drawing layer, draws nothing, and reports no error — the mark is simply
    /// absent, which is indistinguishable from one that was never created.
    /// </summary>
    [Fact]
    public void ANonFinitePositionIsNotAPlace()
    {
        Assert.False(AnnotationAnchor.ImagePixel(double.NaN, 0).IsValid);
        Assert.False(AnnotationAnchor.ImagePixel(0, double.PositiveInfinity).IsValid);
        Assert.False(AnnotationAnchor.Data(0, 0, double.NaN).IsValid);
    }

    /// <summary>A Dec of 120° is not a place, and silently drawing it somewhere is worse than refusing it.</summary>
    [Theory]
    [InlineData(180, 45, true)]
    [InlineData(0, -90, true)]
    [InlineData(359.99, 90, true)]
    [InlineData(360, 0, false)]
    [InlineData(-1, 0, false)]
    [InlineData(180, 120, false)]
    public void SkyCoordinatesAreRangeChecked(double ra, double dec, bool valid)
        => Assert.Equal(valid, AnnotationAnchor.Sky(ra, dec).IsValid);

    /// <summary>An image pixel is not range-checked: a mark off the edge of the frame is still a place.</summary>
    [Fact]
    public void ImagePixelsAreNotRangeChecked()
        => Assert.True(AnnotationAnchor.ImagePixel(-500, 99999).IsValid);

    [Theory]
    [InlineData("sky", AnchorSpace.Sky)]
    [InlineData("ICRS", AnchorSpace.Sky)]
    [InlineData("imagePixel", AnchorSpace.ImagePixel)]
    [InlineData("pixel", AnchorSpace.ImagePixel)]
    [InlineData("voxel", AnchorSpace.Data)]
    public void ASpaceIsParsedFromTheWordsPeopleUse(string text, AnchorSpace expected)
        => Assert.Equal(expected, AnnotationAnchor.ParseSpace(text));

    [Fact]
    public void AnUnknownSpaceIsRefusedRatherThanDefaulted()
        => Assert.Null(AnnotationAnchor.ParseSpace("galactic"));

    // ── Kinds ───────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("box", AnnotationKind.Rect)]
    [InlineData("RECTANGLE", AnnotationKind.Rect)]
    [InlineData("ellipse", AnnotationKind.Circle)]
    [InlineData("label", AnnotationKind.Callout)]
    [InlineData("note", AnnotationKind.Text)]
    public void AKindIsParsedFromTheWordsPeopleUse(string text, AnnotationKind expected)
        => Assert.Equal(expected, AnnotationKindExtensions.Parse(text));

    [Fact]
    public void OnlyShapesNeedASize()
    {
        Assert.True(AnnotationKind.Rect.NeedsExtent());
        Assert.True(AnnotationKind.Circle.NeedsExtent());
        Assert.False(AnnotationKind.Callout.NeedsExtent());
        Assert.False(AnnotationKind.Text.NeedsExtent());
    }

    // ── Validation ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AWellFormedMarkHasNothingWrongWithIt() => Assert.Null(Valid().Validate());

    [Fact]
    public void AShapeWithNoAreaIsRefused()
    {
        var flat = Valid() with { Extent = new Extent(0, 5) };
        Assert.Contains("greater than zero", flat.Validate());
    }

    [Fact]
    public void AShapeWithNoSizeAtAllIsRefusedAndToldWhatToGive()
    {
        var sizeless = Valid() with { Extent = null };
        Assert.Contains("needs a size", sizeless.Validate());
        Assert.Contains("circle", sizeless.Validate());
    }

    /// <summary>A callout with nothing to say is a leader pointing at a blank rule — it reads as a fault.</summary>
    [Fact]
    public void ACalloutNeedsSomethingToSay()
    {
        var mute = Valid() with { Kind = AnnotationKind.Callout, Extent = null, Text = "  " };
        Assert.Contains("needs text", mute.Validate());
    }

    [Fact]
    public void AMarkNeedsAnId()
        => Assert.Contains("needs an id", (Valid() with { Id = "  " }).Validate());

    [Fact]
    public void AnImpossiblePositionIsNamedByItsSpace()
    {
        var offSky = Valid() with { Anchor = AnnotationAnchor.Sky(400, 0) };
        Assert.Contains("sky", offSky.Validate());
    }

    // ── Style ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A colour survives as 8 bits: stored as #rrggbb, shown as #rrggbb, sent over MCP as #rrggbb. A
    /// default with more precision than that is one that cannot come back from its own storage.
    /// </summary>
    [Fact]
    public void EveryDefaultColourSurvivesItsOwnRoundTrip()
    {
        foreach (var style in new[] { MarkStyle.UserDefault, MarkStyle.AgentDefault })
        {
            var back = MarkStyle.ColourFromHex(style.ColourHex());
            Assert.NotNull(back);
            Assert.Equal(style.Red, back!.Value.R, 10);
            Assert.Equal(style.Green, back.Value.G, 10);
            Assert.Equal(style.Blue, back.Value.B, 10);
        }
    }

    [Theory]
    [InlineData("#9ed9ff")]
    [InlineData("9ED9FF")]
    public void AColourIsParsedWithOrWithoutItsHashAndInAnyCase(string hex)
        => Assert.Equal("#9ed9ff", MarkStyle.UserDefault.WithColourHex(hex).ColourHex());

    /// <summary>
    /// A typo must not silently produce black: on a dark image that is a mark that has vanished, with
    /// nothing to say it went wrong.
    /// </summary>
    [Theory]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#gggggg")]
    [InlineData("")]
    public void AColourThatIsNotOneIsRefusedRatherThanRead(string text)
        => Assert.Null(MarkStyle.ColourFromHex(text));

    /// <summary>A zero stroke draws nothing and a zero font is an invisible label — both look like loss.</summary>
    [Fact]
    public void AStyleIsClampedToWhatCanBeDrawnAndRead()
    {
        var absurd = new MarkStyle(5, -2, double.NaN, 0, false, 0).Sane();

        Assert.Equal(1.0, absurd.Red);
        Assert.Equal(0.0, absurd.Green);
        Assert.Equal(0.0, absurd.Blue);
        Assert.Equal(6.0, absurd.FontSize);
        Assert.Equal(0.5, absurd.Stroke);
    }

    [Fact]
    public void AnAgentsMarksAreDistinguishableFromAPersons()
        => Assert.NotEqual(MarkStyle.ForAuthor(MarkAuthor.User).ColourHex(),
                           MarkStyle.ForAuthor(MarkAuthor.Agent).ColourHex());

    /// <summary>
    /// A mark with no style of its own is drawn however its author's marks are drawn — which is what
    /// every mark stored before styling existed means. Defaulting it on load would silently restyle
    /// work people had already done.
    /// </summary>
    [Fact]
    public void AMarkWithoutAStyleFallsBackToItsAuthors()
    {
        var agentMark = Valid() with { Author = MarkAuthor.Agent, Style = null };
        Assert.Equal(MarkStyle.AgentDefault.ColourHex(), agentMark.EffectiveStyle.ColourHex());

        var styled = agentMark with { Style = MarkStyle.UserDefault.WithColourHex("#ff0000") };
        Assert.Equal("#ff0000", styled.EffectiveStyle.ColourHex());
    }

    /// <summary>
    /// A headless surface reports an ink scale of zero — a probe, or an agent asking before the window
    /// is shown. Caught in one place because it is applied in several, and a zero caught in the stroke
    /// but not the leader draws a full-size ring with no leader at all.
    /// </summary>
    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(double.NaN, 1.0)]
    [InlineData(-3.0, 1.0)]
    [InlineData(4.0, 4.0)]
    public void AnUnusableInkFactorFallsBackToTheScreen(double given, double expected)
        => Assert.Equal(expected, MarkStyle.UsableInk(given));

    // ── Carrying a style through the settings store ─────────────────────────────────────────────

    /// <summary>
    /// A style has to come back from its own storage unchanged, or the default a person chose is not
    /// the default they get — which reads as the setting not having been saved at all.
    /// </summary>
    [Fact]
    public void AStyleSurvivesItsOwnEncoding()
    {
        var style = MarkStyle.FromBytes(200, 40, 90, 18, bold: true, stroke: 2.5);

        Assert.Equal(style, MarkStyle.Decode(style.Encode(), MarkStyle.UserDefault));
    }

    /// <summary>
    /// The shipped defaults especially: a constant that does not equal itself after one round trip is
    /// how "reset to default" ends up producing something subtly different.
    /// </summary>
    [Theory]
    [MemberData(nameof(Defaults))]
    public void EveryShippedDefaultSurvivesItsOwnEncoding(MarkStyle style)
        => Assert.Equal(style, MarkStyle.Decode(style.Encode(), MarkStyle.AgentDefault));

    public static TheoryData<MarkStyle> Defaults() => new() { MarkStyle.UserDefault, MarkStyle.AgentDefault };

    [Fact]
    public void BoldSurvivesBothWays()
    {
        var plain = MarkStyle.UserDefault with { Bold = false };
        var bold = MarkStyle.UserDefault with { Bold = true };

        Assert.False(MarkStyle.Decode(plain.Encode(), bold).Bold);
        Assert.True(MarkStyle.Decode(bold.Encode(), plain).Bold);
    }

    /// <summary>
    /// Anything unreadable — a truncated value, a colour that is not one, a future build's longer
    /// format — leaves the marks looking as they would have before the setting existed. It is not an
    /// error worth showing anybody.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("garbage")]
    [InlineData("#9ed9ff|11|0")]                 // too few fields
    [InlineData("#9ed9ff|11|0|1|extra")]         // a later build's
    [InlineData("notacolour|11|0|1")]
    [InlineData("#9ed9ff|wide|0|1")]
    [InlineData("#9ed9ff|11|0|thick")]
    public void AnUnreadableStyleFallsBackRatherThanFailing(string? text)
        => Assert.Equal(MarkStyle.UserDefault, MarkStyle.Decode(text, MarkStyle.UserDefault));

    /// <summary>Stored values are clamped on the way in, so a hand-edited setting cannot hide a mark.</summary>
    [Fact]
    public void ADecodedStyleIsStillDrawable()
    {
        var wild = MarkStyle.Decode("#9ed9ff|900|1|900", MarkStyle.UserDefault);

        Assert.Equal(72, wild.FontSize);
        Assert.Equal(20, wild.Stroke);
    }

    /// <summary>Written in the invariant culture, so a comma-decimal machine can read a dot-decimal file.</summary>
    [Fact]
    public void TheEncodingDoesNotDependOnTheMachinesNumberFormat()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");
            var style = MarkStyle.UserDefault with { Stroke = 2.5 };

            Assert.Contains("2.5", style.Encode());
            Assert.Equal(2.5, MarkStyle.Decode(style.Encode(), MarkStyle.AgentDefault).Stroke);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }
}
