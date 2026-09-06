using Microsoft.UI.Xaml;
using CanfarDesktop.Models;
using CanfarDesktop.ViewModels.CubeViewer;

namespace CanfarDesktop.Views.CubeViewer;

/// <summary>
/// The toolbar's half of the cube's marks: arming the pencil and choosing what it draws.
///
/// Arming it switches to the Slice view rather than being refused in Volume. Placing a mark in a
/// perspective view means guessing a depth from a flat press; the slice already has its channel, so the
/// pencil belongs there — and a control that silently does nothing in the mode you are in is worse than
/// one that takes you where it works.
/// </summary>
public sealed partial class CubeViewerPage
{
    private void OnToggleDrawMarks(object sender, RoutedEventArgs e)
    {
        DrawingArmed = DrawMarksToggle.IsChecked == true;

        if (DrawingArmed)
        {
            if (ViewModel.ViewMode != CubeViewMode.Slice) SetViewMode(CubeViewMode.Slice);
        }
        else
        {
            // Putting the pencil down commits whatever was being labelled. Left open, a callout with no
            // words yet is a mark that cannot be stored and quietly disappears on the next load.
            EndAnnotationEditing();
        }
    }

    private void OnMarkKindChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement item || item.Tag is not string tag) return;
        if (AnnotationKindExtensions.Parse(tag) is not { } kind) return;

        DrawingKind = kind;

        // Choosing a shape is choosing to draw one.
        if (!DrawingArmed)
        {
            DrawingArmed = true;
            DrawMarksToggle.IsChecked = true;
            if (ViewModel.ViewMode != CubeViewMode.Slice) SetViewMode(CubeViewMode.Slice);
        }
    }

    private void OnDeleteSelectedMark(object sender, RoutedEventArgs e) => DeleteSelectedMark();
}
