using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// Who owns a press on the image.
///
/// Three gestures want the left button — pan, draw, and select an area. Each deciding for itself, in
/// whatever order the handlers ran, is what the Linux build records breaking: the pan claimed the
/// sequence and marks could not be placed at all. The interesting cases are the overlaps, so those
/// are what these are.
/// </summary>
public class CanvasPressTests
{
    private static CanvasPress.Intent Nothing => new(false, false, false, false, false);

    private static PressOwner Owner(CanvasPress.Intent intent) => CanvasPress.Owner(intent);

    [Fact]
    public void APlainPressOnTheImageIsTheCanvases()
        => Assert.Equal(PressOwner.Canvas, Owner(Nothing));

    // ── Select-area ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A selection owns every press while armed, INCLUDING one on a mark. The region someone wants
    /// almost always starts on top of something interesting, and marks are what people put on the
    /// interesting things.
    /// </summary>
    [Fact]
    public void AnArmedSelectionOwnsAPressEvenOnAMark()
        => Assert.Equal(PressOwner.Selecting, Owner(Nothing with { SelectingArmed = true, OverMark = true }));

    /// <summary>And it beats the pencil, which is the other armed mode.</summary>
    [Fact]
    public void AnArmedSelectionBeatsThePencil()
        => Assert.Equal(PressOwner.Selecting,
                        Owner(Nothing with { SelectingArmed = true, DrawingArmed = true }));

    /// <summary>The modifier is the way in without putting the mouse down to press a button.</summary>
    [Fact]
    public void TheModifierSelectsWithoutTheModeBeingArmed()
        => Assert.Equal(PressOwner.Selecting, Owner(Nothing with { ModifierSelects = true }));

    // ── Getting out ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// "Move the image" wins over everything. It is how a person pans out of a mode they are in the
    /// middle of, without having to leave the mode first.
    /// </summary>
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    public void ThePanModifierAlwaysWins(bool selecting, bool drawing, bool overMark)
        => Assert.Equal(PressOwner.Canvas, Owner(new CanvasPress.Intent(
            SelectingArmed: selecting, ModifierSelects: true,
            PanModifier: true, DrawingArmed: drawing, OverMark: overMark)));

    // ── Marks ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The pencil stands aside on an existing mark — the press picks that one up rather than drawing
    /// over it — but either way it is the marks' business, not the canvas's.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void ThePencilOrAMarkUnderThePointerMeansTheMarks(bool drawing, bool overMark)
        => Assert.Equal(PressOwner.Drawing, Owner(Nothing with { DrawingArmed = drawing, OverMark = overMark }));

    /// <summary>With no pencil and no mark there, the press pans.</summary>
    [Fact]
    public void EmptyImageWithNothingArmedPans()
        => Assert.Equal(PressOwner.Canvas, Owner(Nothing with { DrawingArmed = false, OverMark = false }));

    // ── Every combination resolves ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Whatever the state, exactly one gesture gets the press. The bug this replaces was two of them
    /// each believing they had it.
    /// </summary>
    [Fact]
    public void EveryCombinationHasExactlyOneOwner()
    {
        for (var bits = 0; bits < 32; bits++)
        {
            var intent = new CanvasPress.Intent(
                SelectingArmed: (bits & 1) != 0,
                ModifierSelects: (bits & 2) != 0,
                PanModifier: (bits & 4) != 0,
                DrawingArmed: (bits & 8) != 0,
                OverMark: (bits & 16) != 0);

            var owner = Owner(intent);
            Assert.True(Enum.IsDefined(owner), $"{intent} gave {owner}");
        }
    }
}
