using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.Models;

namespace CanfarDesktop.Views.FitsViewer;

/// <summary>
/// The toolbar's half of the marks: arming the pencil, choosing what it draws, and deleting one.
///
/// The state lives on the HOST rather than on a page, so it survives switching tabs. Arming the pencil,
/// looking at another image and coming back to find it disarmed would read as the toggle having been
/// forgotten — and the toggle is the one thing telling you a press will draw rather than pan.
/// </summary>
public sealed partial class FitsTabHost
{
    private bool _drawMarksArmed;
    private AnnotationKind _markKind = AnnotationKind.Circle;

    private void OnToggleDrawMarks(object sender, RoutedEventArgs e)
    {
        _drawMarksArmed = DrawMarksToggle.IsChecked == true;
        ApplyMarkModeToActivePage();

        // Putting the pencil down commits whatever was being labelled. Left open, a callout with no text
        // yet is a mark that cannot be stored and quietly disappears on the next load.
        if (!_drawMarksArmed) _activePage?.EndAnnotationEditing();
    }

    private void OnMarkKindChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement item || item.Tag is not string tag) return;
        if (AnnotationKindExtensions.Parse(tag) is not { } kind) return;

        _markKind = kind;
        ApplyMarkModeToActivePage();

        // Choosing a shape is choosing to draw one. Making someone then find the pencil is a step that
        // exists only because the two controls are separate.
        if (!_drawMarksArmed)
        {
            _drawMarksArmed = true;
            DrawMarksToggle.IsChecked = true;
            ApplyMarkModeToActivePage();
        }
    }

    private void OnDeleteSelectedMark(object sender, RoutedEventArgs e) => _activePage?.DeleteSelectedMark();

    /// <summary>
    /// Export the view on screen. A region is Ctrl-drag on the image rather than a second button: the
    /// gesture and the figure are one action, and a "select a region" mode would be a mode to leave.
    /// </summary>
    private void OnExportFigure(object sender, RoutedEventArgs e) => _ = _activePage?.ExportCurrentViewAsync();

    /// <summary>Push the pencil's state onto the page on screen. Called on every tab switch too.</summary>
    private void ApplyMarkModeToActivePage()
    {
        if (_activePage is null) return;

        _activePage.DrawingArmed = _drawMarksArmed;
        _activePage.DrawingKind = _markKind;
    }
}
