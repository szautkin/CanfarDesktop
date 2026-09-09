using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.Models;
using CanfarDesktop.Services;

namespace CanfarDesktop.Views.FitsViewer;

/// <summary>
/// The Marks panel's half of this image's marks: what its controls mean here.
///
/// The same control the cube uses, wired the same way. The two viewers differ only in where their marks
/// come from — a flat image's pixels against a cube's voxels — and a second panel would be a second
/// place to fix a bug in the list, and a second set of habits to learn.
/// </summary>
public sealed partial class FitsViewerPage
{
    private bool _marksWired;

    private void OnMarksExpanding(Expander sender, ExpanderExpandingEventArgs args)
    {
        WireMarksPanel();
        RefreshMarksPanel();
    }

    private void OnMarksCollapsed(Expander sender, ExpanderCollapsedEventArgs args)
    {
        // Collapsing the section puts the pencil down. Leaving it armed would mean a press on the image
        // still draws with no visible control saying so — which reads as the viewer having broken its
        // own panning.
        SetDrawArmed(false);
        EndAnnotationEditing();
    }

    /// <summary>
    /// Connect the panel once. Its events outlive any one image, so re-subscribing on every expand
    /// would fire each handler as many times as the section had been opened.
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
            RenderAnnotations();
            RefreshMarksPanel();
        };

        Marks.EditRequested += id =>
        {
            _selectedId = id;
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
    /// Public because the tab host's toolbar arms it too: the host owns the state across tabs, so that
    /// a pencil armed on one image is still armed when you come back to it.
    /// </summary>
    public void SetDrawArmed(bool armed)
    {
        DrawingArmed = armed;
        Marks.DrawArmed = armed;

        if (!armed) EndAnnotationEditing();
    }

    /// <summary>Open the Marks section — what the toolbar's one Marks button does.</summary>
    public void ShowMarksPanel()
    {
        ShowHeaderColumn();
        MarksExpander.IsExpanded = true;
    }

    /// <summary>Show the panel what there is, if the section is open.</summary>
    private void RefreshMarksPanel()
    {
        if (MarksExpander is null || !MarksExpander.IsExpanded) return;

        EnsureAnnotationsLoaded();
        Marks.Show(_annotations, _selectedId);
        Marks.Kind = DrawingKind;
    }

    /// <summary>
    /// What the next mark will look like: the style row when the section is open, the stored default
    /// otherwise.
    /// </summary>
    private MarkStyle PendingStyle()
    {
        if (MarksExpander?.IsExpanded == true) return Marks.Style();

        try
        {
            var settings = App.Services.GetService(typeof(ISettingsService)) as ISettingsService;
            return MarkStyle.Decode(settings?.DefaultMarkStyle, MarkStyle.UserDefault);
        }
        catch
        {
            return MarkStyle.UserDefault;
        }
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
