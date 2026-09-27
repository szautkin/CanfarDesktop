using Microsoft.UI.Xaml.Controls;

namespace CanfarDesktop.Views.FitsViewer;

/// <summary>
/// Opening the Marks section, and handing the panel to the mark editor.
///
/// The wiring itself is not here: the panel's controls mean the same thing on every viewer, so what
/// each of its events does lives once, in MarkEditor.Attach.
/// </summary>
public sealed partial class FitsViewerPage
{
    private void OnMarksExpanding(Expander sender, ExpanderExpandingEventArgs args)
    {
        if (_panelBinding is null)
        {
            _panelBinding = Views.Controls.MarkPanelBinding.Attach(MarksPanelControl, Marks, this);

            // Once, with the binding: the editor raises Changed on every edit AND on every change of
            // extension, which is exactly when the count of marks on the other chips can move.
            Marks.Changed += () => MarksPanelControl.MarksElsewhere = MarksOnOtherExtensions();
        }

        _panelBinding.Show();
        MarksPanelControl.MarksElsewhere = MarksOnOtherExtensions();
    }

    /// <summary>How many marks this file holds on extensions other than the one on screen.</summary>
    private int MarksOnOtherExtensions()
    {
        if (_annotationStore is null || Target is not { } here) return 0;

        return _annotationStore.Targets()
            .Where(key => Helpers.MarkTarget.SameFile(key, here) && !Helpers.MarkTarget.SameKey(key, here))
            .Sum(key => _annotationStore.LoadFor(key).Count);
    }

    private void OnMarksCollapsed(Expander sender, ExpanderCollapsedEventArgs args)
    {
        // Collapsing the section puts the pencil down. Leaving it armed would mean a press on the image
        // still draws with no visible control saying so — which reads as the viewer having broken its
        // own panning.
        Marks.SetDrawArmed(false);
    }

    /// <summary>Open the Marks section — what the toolbar's one Marks button does.</summary>
    public void ShowMarksPanel()
    {
        ShowHeaderColumn();
        MarksExpander.IsExpanded = true;
    }

    /// <summary>
    /// Close the Marks section again.
    ///
    /// The collapse handler puts the pencil down, so closing the panel cannot leave a press on the
    /// image still drawing with no visible control saying so.
    /// </summary>
    public void HideMarksPanel() => MarksExpander.IsExpanded = false;

    /// <summary>
    /// Arm or disarm the pencil.
    ///
    /// Public because the tab host's toolbar can arm it too, and because the host pushes the mode onto
    /// whichever page is on screen.
    /// </summary>
    public void SetDrawArmed(bool armed) => Marks.SetDrawArmed(armed);
}
