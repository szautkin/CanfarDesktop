using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Views.FitsViewer;

/// <summary>
/// The toolbar as a strip that scrolls sideways when it is wider than the window.
///
/// <para>Every decision — whether there is anything out of sight, where a step lands — is
/// <see cref="ToolbarOverflow"/>, which has no window in it. This half only reads three numbers off the
/// scroller and hands the answers back to it.</para>
/// </summary>
public sealed partial class FitsTabHost
{
    private (double Offset, double Viewport, double Extent) ToolbarMeasure()
        => (ToolbarScroller.HorizontalOffset, ToolbarScroller.ViewportWidth, ToolbarScroller.ExtentWidth);

    private void OnToolbarSizeChanged(object sender, SizeChangedEventArgs e) => UpdateToolbarArrows();

    private void OnToolbarViewChanged(object? sender, ScrollViewerViewChangedEventArgs e) => UpdateToolbarArrows();

    /// <summary>
    /// Show each step button only while there is something out of sight in its direction.
    ///
    /// A button that is always there says the strip overflows when it does not, and one that stays
    /// after the end is reached is a button that does nothing when pressed.
    /// </summary>
    private void UpdateToolbarArrows()
    {
        var (offset, viewport, extent) = ToolbarMeasure();

        ToolbarBackButton.Visibility = ToolbarOverflow.CanScrollBack(offset, viewport, extent)
            ? Visibility.Visible : Visibility.Collapsed;
        ToolbarForwardButton.Visibility = ToolbarOverflow.CanScrollForward(offset, viewport, extent)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnToolbarBack(object sender, RoutedEventArgs e)
    {
        var (offset, viewport, extent) = ToolbarMeasure();
        ToolbarScroller.ChangeView(ToolbarOverflow.Back(offset, viewport, extent), null, null);
    }

    private void OnToolbarForward(object sender, RoutedEventArgs e)
    {
        var (offset, viewport, extent) = ToolbarMeasure();
        ToolbarScroller.ChangeView(ToolbarOverflow.Forward(offset, viewport, extent), null, null);
    }

    /// <summary>
    /// A plain wheel over the strip scrolls it sideways.
    ///
    /// Only Shift-wheel moves a horizontal scroller on its own, and nobody reaching for a tool they
    /// cannot see thinks to hold Shift. When the strip already fits, the wheel is left alone so it
    /// still reaches whatever is underneath.
    /// </summary>
    private void OnToolbarWheel(object sender, PointerRoutedEventArgs e)
    {
        var (offset, viewport, extent) = ToolbarMeasure();
        if (!ToolbarOverflow.HasOverflow(viewport, extent)) return;

        var delta = e.GetCurrentPoint(ToolbarScroller).Properties.MouseWheelDelta;
        ToolbarScroller.ChangeView(
            ToolbarOverflow.Clamp(offset - delta, viewport, extent), null, null, disableAnimation: true);
        e.Handled = true;
    }
}
