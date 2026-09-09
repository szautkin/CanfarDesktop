using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
/// Both views can be drawn on. A press on the slice names one voxel outright; a press on the volume
/// names a ray, so the channel on screen supplies the depth and the mark lands on the plane the volume
/// already draws as the slice-plane marker. Forcing the Slice view when the pencil was armed was the
/// earlier answer, and it moved the view out from under someone who had lined up an angle they wanted.
/// </summary>
public sealed partial class CubeViewerPage
{
    private bool _marksWired;

    /// <summary>Whether each section of the control column is showing.</summary>
    private bool _displayOpen = true;
    private bool _marksOpen;

    /// <summary>
    /// The open section's title, bright. A closed one is dimmed back.
    ///
    /// No bar behind it: which section is open is already said by the section BEING there, so the title
    /// only has to lift. A filled highlight on a panel that sits on top of the image is one more opaque
    /// rectangle competing with the data.
    /// </summary>
    private static readonly SolidColorBrush SectionTitleOpen = new(Windows.UI.Color.FromArgb(0xFF, 0xDC, 0xF2, 0xFF));

    private static readonly SolidColorBrush SectionTitleClosed = new(Windows.UI.Color.FromArgb(0x90, 0xBF, 0xD8, 0xFF));

    /// <summary>
    /// The DISPLAY section's own header.
    ///
    /// Collapsing it is how the column gets out of the way without anything being hidden from you: the
    /// header stays, so the controls are one press from coming back. That matters more here than in a
    /// sidebar, because this column sits ON the image it is describing.
    /// </summary>
    private void OnToggleDisplaySection(object sender, RoutedEventArgs e)
    {
        _displayOpen = !_displayOpen;
        ApplySectionState(_displayOpen, DisplaySection, DisplayTitle, DisplayChevron);
    }

    /// <summary>The MARKS section's own header. The toolbar button is the other way in.</summary>
    private void OnToggleMarksSection(object sender, RoutedEventArgs e)
    {
        _marksOpen = !_marksOpen;
        ApplySectionState(_marksOpen, Marks, MarksTitle, MarksChevron);
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
        _marksOpen = MarksPanelToggle.IsChecked == true;
        ApplySectionState(_marksOpen, Marks, MarksTitle, MarksChevron);
        SyncMarksSection(bringIntoView: true);
    }

    /// <summary>Show or hide a section's body, brighten its title, and point its chevron.</summary>
    private static void ApplySectionState(bool open, UIElement body, TextBlock title, FontIcon chevron)
    {
        body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        title.Foreground = open ? SectionTitleOpen : SectionTitleClosed;

        // Up when open (press to close), down when closed (press to open) — the chevron shows what the
        // next press does, which is the convention every disclosure control uses.
        chevron.Glyph = open ? "" : "";
        chevron.Foreground = title.Foreground;
    }

    private void SyncMarksSection(bool bringIntoView)
    {
        // The toolbar button and the section header are two ways to the same state, so neither may be
        // left saying something the other has just contradicted.
        if (MarksPanelToggle.IsChecked != _marksOpen) MarksPanelToggle.IsChecked = _marksOpen;

        if (_marksOpen)
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

    /// <summary>
    /// Arm or disarm the pencil, keeping the panel's toggle in step.
    ///
    /// It no longer forces the Slice view. Both views can be drawn on: a press on the volume names a
    /// ray, and the channel on screen supplies the depth — so a mark lands on the plane the volume is
    /// already drawing as the slice-plane marker, which is a depth you can see before you press.
    /// </summary>
    private void SetDrawArmed(bool armed)
    {
        DrawingArmed = armed;
        Marks.DrawArmed = armed;

        // Putting the pencil down commits whatever was being labelled. Left open, a mark with no words
        // yet is one that cannot be stored and quietly disappears on the next load.
        if (!armed) EndAnnotationEditing();
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
