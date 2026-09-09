using Microsoft.UI.Xaml;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Services;
using CanfarDesktop.ViewModels.CubeViewer;

namespace CanfarDesktop.Views.CubeViewer;

/// <summary>
/// The Marks panel's half of the cube's marks: opening it, and what its controls mean here.
///
/// Everything a person does to a mark that is not a gesture on the image happens in that one panel —
/// arming the pencil, choosing the shape, styling it, finding one in the list, renaming and deleting.
/// It replaced two unlabelled toolbar icons that were the feature's only entry point.
///
/// Arming the pencil switches to the Slice view rather than being refused in Volume. Placing a mark in
/// a perspective view means guessing a depth from a flat press; the slice already has its channel, so
/// the pencil belongs there — and a control that silently does nothing in the mode you are in is worse
/// than one that takes you where it works.
/// </summary>
public sealed partial class CubeViewerPage
{
    private bool _marksWired;

    private void OnToggleMarksPanel(object sender, RoutedEventArgs e)
    {
        var open = MarksPanelToggle.IsChecked == true;
        MarksPanelHost.Visibility = open ? Visibility.Visible : Visibility.Collapsed;

        if (open)
        {
            WireMarksPanel();
            RefreshMarksPanel();
        }
        else
        {
            // Closing the panel puts the pencil down. Leaving it armed would mean a press on the slice
            // still draws with no visible control saying so — which reads as the viewer having broken
            // its own panning.
            SetDrawArmed(false);
            EndAnnotationEditing();
        }
    }

    /// <summary>
    /// Connect the panel once. Its events outlive any one cube, so re-subscribing on every open would
    /// fire each handler as many times as the panel had been opened.
    /// </summary>
    private void WireMarksPanel()
    {
        if (_marksWired) return;
        _marksWired = true;

        Marks.DrawArmedChanged += SetDrawArmed;

        Marks.KindChanged += kind => DrawingKind = kind;

        Marks.StyleChanged += style =>
        {
            // Acting on the selected mark when there is one, and on what the next mark will look like
            // otherwise — which is how every drawing application behaves.
            if (_selectedId is { } id && _annotations.FindIndex(a => a.Id == id) is >= 0 and var at)
            {
                _annotations[at] = _annotations[at] with { Style = style };
                SaveAnnotations();
                RenderAnnotations();
            }
            else
            {
                RememberDefaultStyle(style);
            }
        };

        Marks.SelectionChanged += id =>
        {
            _selectedId = id;

            // Selecting a row is how you find a mark again after panning away from it, so this goes to
            // the mark rather than only highlighting the row.
            if (id is not null) GoToMark(id);
            RenderAnnotations();
            RefreshMarksPanel();
        };

        Marks.EditRequested += id =>
        {
            _selectedId = id;
            if (ViewModel.ViewMode != CubeViewMode.Slice) SetViewMode(CubeViewMode.Slice);
            GoToMark(id);
            BeginLabelEdit(id);
        };

        Marks.DeleteRequested += id =>
        {
            _annotations.RemoveAll(a => a.Id == id);
            if (_selectedId == id) _selectedId = null;
            SaveAnnotations();
            RenderAnnotations();
            RefreshMarksPanel();
        };

        Marks.ClearAllRequested += () =>
        {
            _annotations.Clear();
            _selectedId = null;
            _editingId = null;
            SaveAnnotations();
            RenderAnnotations();
            RefreshMarksPanel();
        };
    }

    /// <summary>Arm or disarm the pencil, keeping the panel's toggle and the view mode in step.</summary>
    private void SetDrawArmed(bool armed)
    {
        DrawingArmed = armed;
        Marks.DrawArmed = armed;

        if (armed)
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

    /// <summary>Show the panel what there is, if it is open. Cheap enough to call after any change.</summary>
    private void RefreshMarksPanel()
    {
        if (MarksPanelHost.Visibility != Visibility.Visible) return;

        EnsureAnnotationsLoaded();
        Marks.Show(_annotations, _selectedId);
        Marks.Kind = DrawingKind;
    }

    /// <summary>
    /// Bring a mark into view: its channel, since that is what decides whether the slice draws it at all.
    ///
    /// Panning to centre it as well would fight whatever the person had lined up; the channel is the one
    /// part they cannot recover by looking.
    /// </summary>
    private void GoToMark(string id)
    {
        var mark = _annotations.FirstOrDefault(a => a.Id == id);
        if (mark is null || mark.Anchor.Space != AnchorSpace.Data) return;

        // Away-from-zero, matching the slice surface's own channel test: banker's rounding sends
        // channel 16.5 to 16 there and 16 here, or the mark is shown on a slice that does not draw it.
        var channel = (int)Math.Round(mark.Anchor.Z, MidpointRounding.AwayFromZero);
        var last = Math.Max(0, (_volume?.Nz ?? 1) - 1);
        ViewModel.Channel = Math.Clamp(channel, 0, last);
    }

    /// <summary>
    /// Remember what the next mark should look like.
    ///
    /// A preference, not the storage: every mark keeps its own style, because it persists, travels over
    /// MCP and ends up in an exported figure that has to look the same when reopened.
    /// </summary>
    private static void RememberDefaultStyle(MarkStyle style)
    {
        try
        {
            if (App.Services.GetService(typeof(ISettingsService)) is not ISettingsService settings) return;
            settings.DefaultMarkStyle = style.Encode();
            settings.Save();
        }
        catch
        {
            // A preference that could not be stored is not worth interrupting drawing for.
        }
    }
}
