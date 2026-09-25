namespace CanfarDesktop.Helpers;

/// <summary>Which gesture a press on the image canvas belongs to.</summary>
public enum PressOwner
{
    /// <summary>The image itself: pan, or nothing.</summary>
    Canvas,

    /// <summary>A mark — placing one, or taking hold of one.</summary>
    Drawing,

    /// <summary>Dragging out a region of the image.</summary>
    Selecting,
}

/// <summary>
/// Who owns a press, decided once.
///
/// <para>Three gestures want the left button — pan, draw, and select an area — and each of them used
/// to decide for itself, in the order the handlers happened to run. The Linux build records what that
/// cost: the pan claimed the sequence and marks could not be placed at all. Asking the question in one
/// place is the fix, and the answer is worth testing because the interesting cases are the overlaps.</para>
///
/// <para>Free of any UI type: it is the decision, not the handling.</para>
/// </summary>
public static class CanvasPress
{
    /// <summary>What a press is asking for.</summary>
    /// <param name="SelectingArmed">Select-area mode is on.</param>
    /// <param name="ModifierSelects">
    /// The select-area modifier is held. The mode is the discoverable way in; the modifier is the one
    /// that does not make you put the mouse down and come back.
    /// </param>
    /// <param name="PanModifier">
    /// The "move the image, not its contents" modifier. It wins over everything, because it is how a
    /// person gets out of a mode they are in the middle of without leaving it.
    /// </param>
    /// <param name="DrawingArmed">The pencil is down: a press on empty image makes a mark.</param>
    /// <param name="OverMark">A mark is under the pointer.</param>
    public readonly record struct Intent(
        bool SelectingArmed,
        bool ModifierSelects,
        bool PanModifier,
        bool DrawingArmed,
        bool OverMark);

    /// <summary>
    /// Who gets it.
    ///
    /// <para>Select-area owns EVERY press while it is armed, marks included. Drawing stands aside on a
    /// mark so an existing one can still be picked up, but a selection cannot afford to: the region you
    /// want almost always starts on top of something interesting, and marks are what people put on the
    /// interesting things.</para>
    /// </summary>
    public static PressOwner Owner(Intent intent)
    {
        // "Move the image" is the way out of any mode, so it is asked first.
        if (intent.PanModifier) return PressOwner.Canvas;

        if (intent.SelectingArmed || intent.ModifierSelects) return PressOwner.Selecting;

        // The pencil makes a mark on empty image; on an existing mark, the press picks that one up,
        // which is still the marks' business rather than the canvas's.
        if (intent.DrawingArmed || intent.OverMark) return PressOwner.Drawing;

        return PressOwner.Canvas;
    }
}
