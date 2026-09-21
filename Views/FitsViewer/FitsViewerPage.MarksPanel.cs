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
        _panelBinding ??= Views.Controls.MarkPanelBinding.Attach(MarksPanelControl, Marks, this);
        _panelBinding.Show();
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
    /// Arm or disarm the pencil.
    ///
    /// Public because the tab host's toolbar can arm it too, and because the host pushes the mode onto
    /// whichever page is on screen.
    /// </summary>
    public void SetDrawArmed(bool armed) => Marks.SetDrawArmed(armed);
}
