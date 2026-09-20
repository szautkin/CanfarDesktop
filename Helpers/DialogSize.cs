namespace CanfarDesktop.Helpers;

/// <summary>
/// How big a dialog may be, given the window it has to fit inside.
///
/// <para>The export dialogs asked for a fixed thousand by six hundred. On a roomy screen that is the
/// right size; on a small window it is simply bigger than the space available, and a
/// <c>ContentDialog</c> does not shrink its content to fit — it clips it. What got clipped was the
/// bottom of the options column, which is where the export buttons were, so on a short window the
/// PDF button was half off the dialog and no amount of scrolling reached it: the scroll area itself
/// extended past the edge.</para>
///
/// <para>So the dialog asks for what it wants and settles for what there is. Separate from either
/// dialog because both do it and neither should be the one that owns the rule.</para>
/// </summary>
public static class DialogSize
{
    /// <summary>
    /// Room to leave around the dialog: the window's own chrome, the dialog's title and its command
    /// bar, and a margin so it does not sit flush against the edges.
    /// </summary>
    public const double Chrome = 160.0;

    /// <summary>
    /// The size to use along one axis.
    ///
    /// <para>Never larger than the space there is, never smaller than the dialog needs to be usable.
    /// When those two conflict the minimum wins and the dialog is clipped — but by then the window is
    /// smaller than anything could be laid out in, and a dialog squeezed to nothing would be worse
    /// than one that overflows.</para>
    ///
    /// <para>An unmeasurable window means the preferred size: a dialog opening before its root has
    /// been measured should look right rather than defensively small.</para>
    /// </summary>
    public static double Fit(double available, double preferred, double minimum, double chrome = Chrome)
    {
        if (!double.IsFinite(available) || available <= 0) return preferred;

        var room = available - (double.IsFinite(chrome) && chrome > 0 ? chrome : 0);
        return Math.Max(minimum, Math.Min(preferred, room));
    }
}
