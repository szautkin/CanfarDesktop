namespace CanfarDesktop.Helpers;

/// <summary>
/// How far a floating panel has to travel to get out of the way, and how much of it stays behind.
///
/// <para>The cube's panels sit OVER the render, so on a narrow window they swallow the picture. They
/// could simply be hidden, but a panel that vanishes takes its own existence with it: nothing says the
/// controls are still there, or where they went. Sliding each one to its nearest edge and leaving a
/// sliver showing says both — the picture is clear, and the panel is plainly parked rather than gone.</para>
///
/// <para>Sliding also leaves <c>Visibility</c> alone, which matters more than it sounds: the info
/// panel is hidden when a cube has no metadata and the scrubber when a cube has one channel. Those
/// rules already exist and are none of this feature's business. Moving a panel cannot fight them.</para>
/// </summary>
public static class PanelSlide
{
    /// <summary>
    /// How much of a parked panel stays on screen.
    ///
    /// Enough to see, not enough to read — it is a reminder, not a control. A panel that left nothing
    /// behind would be indistinguishable from one that had been removed.
    /// </summary>
    public const double Peek = 5.0;

    /// <summary>
    /// How far a panel must travel to sit against its edge with only <see cref="Peek"/> showing.
    ///
    /// <paramref name="margin"/> is the gap the panel keeps from that edge when it is out: the panel
    /// has to cross its own width AND that gap. Always a distance, never a direction — which edge it
    /// is going to is the caller's business, and the sign belongs there.
    /// </summary>
    public static double Offset(double extent, double margin, double peek = Peek)
    {
        if (!double.IsFinite(extent) || extent <= 0) return 0;

        var gap = double.IsFinite(margin) && margin > 0 ? margin : 0;
        var showing = double.IsFinite(peek) && peek > 0 ? peek : 0;

        // A panel narrower than the sliver is already smaller than the reminder would be, so there is
        // nothing to gain by moving it.
        return Math.Max(0, extent + gap - showing);
    }
}
