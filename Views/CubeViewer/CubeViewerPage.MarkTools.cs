using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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

    /// <summary>
    /// Cap the control column at the height actually available.
    ///
    /// The column sizes to its content, so a short one is a short card. Without a ceiling it kept
    /// growing: in Volume mode the panel ran off the bottom of the window and the opacity curve could
    /// not be reached, because the ScrollViewer inside it was being handed infinite height and so never
    /// had anything to scroll.
    /// </summary>
    private void OnControlColumnBounds(object sender, SizeChangedEventArgs e)
    {
        var margins = ControlColumn.Margin.Top + ControlColumn.Margin.Bottom;
        ControlColumn.MaxHeight = Math.Max(120, e.NewSize.Height - margins);
    }

    /// <summary>
    /// The DISPLAY section's own header.
    ///
    /// Collapsing it is how the column gets out of the way without anything being hidden from you: the
    /// header stays, so the controls are one press from coming back. That matters more here than in a
    /// sidebar, because this column sits ON the image it is describing.
    /// </summary>
    private void OnToggleDisplaySection(object sender, RoutedEventArgs e)
        => ApplySectionState(DisplayHeader, DisplaySection, DisplayChevron);

    /// <summary>The MARKS section's own header. The toolbar button is the other way in.</summary>
    private void OnToggleMarksSection(object sender, RoutedEventArgs e)
    {
        ApplySectionState(MarksHeader, Marks, MarksChevron);
        SyncMarksSection(bringIntoView: false);
    }

    /// <summary>
    /// The toolbar's Marks button.
    ///
    /// It opens the section and brings it into view rather than only expanding it. With DISPLAY open —
    /// which in Volume mode is taller than the column — an expanded MARKS would otherwise be below the
    /// fold, and a button that appears to do nothing is worse than no button.
    /// </summary>
    private void OnToggleMarksPanel(object sender, RoutedEventArgs e)
    {
        MarksHeader.IsChecked = MarksPanelToggle.IsChecked == true;
        ApplySectionState(MarksHeader, Marks, MarksChevron);
        SyncMarksSection(bringIntoView: true);
    }

    /// <summary>Show or hide a section's body, and point its chevron the way it will go next.</summary>
    private static void ApplySectionState(ToggleButton header, UIElement body, FontIcon chevron)
    {
        var open = header.IsChecked == true;
        body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;

        // Down when open (press to close), up when closed (press to open) — the chevron shows the
        // DIRECTION of the next press, which is the convention every disclosure control uses.
        chevron.Glyph = open ? "" : "";
    }

    private void SyncMarksSection(bool bringIntoView)
    {
        var open = MarksHeader.IsChecked == true;

        // The toolbar button and the section header are two ways to the same state, so neither may be
        // left saying something the other has just contradicted.
        if (MarksPanelToggle.IsChecked != open) MarksPanelToggle.IsChecked = open;

        if (open)
        {
            WireMarksPanel();
            RefreshMarksPanel();

            if (bringIntoView) MarksHeader.StartBringIntoView();
        }
        else
        {
            // Closing the section puts the pencil down. Leaving it armed would mean a press on the
            // slice still draws with no visible control saying so — which reads as the viewer having
            // broken its own panning.
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

    /// <summary>Show the panel what there is, if the section is open. Cheap enough to call after any change.</summary>
    private void RefreshMarksPanel()
    {
        if (Marks.Visibility != Visibility.Visible) return;

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
