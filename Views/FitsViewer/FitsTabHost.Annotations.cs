using Microsoft.UI.Xaml;

namespace CanfarDesktop.Views.FitsViewer;

/// <summary>
/// The toolbar's half of the marks: one button, which opens the Marks panel on the image in front.
///
/// Everything else about a mark — arming the pencil, the shape, the style, the list, renaming and
/// deleting — lives in that panel. It used to be split between two unlabelled toolbar icons and nowhere
/// at all, which meant the only way to discover that images could be annotated was to press a picture
/// of a pencil and see what happened.
///
/// The pencil is armed per image rather than per window. A drawing mode attached to a particular
/// picture is what a person expects, and it stops a pencil left armed on one image from turning the
/// next tab's first press into a mark nobody asked for.
/// </summary>
public sealed partial class FitsTabHost
{
    private void OnOpenMarksPanel(object sender, RoutedEventArgs e) => _activePage?.ShowMarksPanel();

    /// <summary>
    /// Export the view on screen. A region is Ctrl-drag on the image rather than a second button: the
    /// gesture and the figure are one action, and a "select a region" mode would be a mode to leave.
    /// </summary>
    private void OnExportFigure(object sender, RoutedEventArgs e) => _ = _activePage?.ExportCurrentViewAsync();
}
