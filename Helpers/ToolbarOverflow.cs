namespace CanfarDesktop.Helpers;

/// <summary>
/// A toolbar that is wider than the window it is in: which way out of it there is, and where a step
/// should land.
///
/// <para>The FITS toolbar carries thirty-nine controls. None of them collapses, so on a narrow window
/// the last of them were not merely awkward to reach — they were off the end of the strip with nothing
/// to scroll and no sign they existed. Whatever the strip does about that, the arithmetic is the same
/// for every toolbar, and it is arithmetic: three numbers in, one number out.</para>
///
/// <para>Here rather than in the control so it can be tested without a window. Offsets, viewports and
/// extents are all in the same units — whatever the caller measures in.</para>
/// </summary>
public static class ToolbarOverflow
{
    /// <summary>
    /// How much of the old view a step leaves behind.
    ///
    /// A step of exactly one viewport puts a fresh screenful in front of you and no landmark from the
    /// one before, so a control sitting on the seam is stepped clean over. Roughly one button's worth
    /// of overlap is enough to keep your place.
    /// </summary>
    public const double Overlap = 48;

    /// <summary>Half a pixel. Below this, a difference is rounding rather than a reason to show a button.</summary>
    private const double Epsilon = 0.5;

    /// <summary>The furthest the strip can be scrolled. Zero when everything already fits.</summary>
    public static double MaxOffset(double viewport, double extent)
    {
        if (!double.IsFinite(viewport) || !double.IsFinite(extent)) return 0;
        return Math.Max(0, extent - Math.Max(0, viewport));
    }

    /// <summary>Whether there is anything out of sight at all — the question the affordances hang on.</summary>
    public static bool HasOverflow(double viewport, double extent)
        => MaxOffset(viewport, extent) > Epsilon;

    /// <summary>Offsets outside the scrollable range are not positions the strip can be in.</summary>
    public static double Clamp(double offset, double viewport, double extent)
        => double.IsFinite(offset) ? Math.Clamp(offset, 0, MaxOffset(viewport, extent)) : 0;

    public static bool CanScrollBack(double offset, double viewport, double extent)
        => Clamp(offset, viewport, extent) > Epsilon;

    public static bool CanScrollForward(double offset, double viewport, double extent)
        => Clamp(offset, viewport, extent) < MaxOffset(viewport, extent) - Epsilon;

    /// <summary>Where a step towards the start of the strip lands.</summary>
    public static double Back(double offset, double viewport, double extent)
        => Step(offset, viewport, extent, -1);

    /// <summary>Where a step towards the end of the strip lands.</summary>
    public static double Forward(double offset, double viewport, double extent)
        => Step(offset, viewport, extent, +1);

    /// <summary>
    /// A step is a viewport less the overlap — except on a strip so narrow that the overlap would eat
    /// it, where it is half the viewport instead. Half of very little is still progress; a fixed
    /// subtraction there produces a step of nothing, and a button that does nothing is worse than no
    /// button at all.
    /// </summary>
    private static double Page(double viewport)
    {
        if (!double.IsFinite(viewport) || viewport <= 0) return 0;
        return Math.Max(viewport / 2, viewport - Overlap);
    }

    private static double Step(double offset, double viewport, double extent, int direction)
        => Clamp(Clamp(offset, viewport, extent) + direction * Page(viewport), viewport, extent);
}
