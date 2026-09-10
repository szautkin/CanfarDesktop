using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CanfarDesktop.Views.CubeViewer;

/// <summary>
/// The control column's two sections, and the toolbar button that opens the Marks one.
///
/// What the Marks panel's controls DO is not here: they mean the same thing on every viewer, so that
/// lives once, in MarkEditor.Attach. This is only the chrome around it — which section is showing, and
/// how the column gets out of the way.
/// </summary>
public sealed partial class CubeViewerPage
{
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
    /// Collapsing it is how the column gets out of the way without anything being hidden: the header
    /// stays, so the controls are one press from coming back. That matters more here than in a sidebar,
    /// because this column sits ON the image it is describing.
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
        ApplySectionState(_marksOpen, MarksPanelControl, MarksTitle, MarksChevron);
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
        ApplySectionState(_marksOpen, MarksPanelControl, MarksTitle, MarksChevron);
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
            _panelBinding ??= Views.Controls.MarkPanelBinding.Attach(MarksPanelControl, Marks);
            _panelBinding.Show();

            // The header to the TOP of the scroller, not merely on screen: brought just into view it
            // ends up at the bottom edge with everything it heads still below the fold, which is a
            // button that appears to have scrolled to nothing.
            //
            // After the layout, not during it. The section has only just been made visible, so the
            // scroller does not yet have the extent to scroll into and the request quietly does nothing.
            if (bringIntoView)
                DispatcherQueue.TryEnqueue(() => MarksHeader.StartBringIntoView(new BringIntoViewOptions
                {
                    VerticalAlignmentRatio = 0,
                    AnimationDesired = true,
                }));
        }
        else
        {
            // Closing the section puts the pencil down. Leaving it armed would mean a press still draws
            // with no visible control saying so — which reads as the viewer having broken its own
            // panning.
            Marks.SetDrawArmed(false);
        }
    }
}
