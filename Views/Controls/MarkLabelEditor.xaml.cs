using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CanfarDesktop.Views.Controls;

/// <summary>
/// The field you name a mark in — see MarkLabelEditor.xaml for why it is not a flyout.
///
/// This half is what its three answers mean. Commit is the tick or Enter, which is what a person presses
/// without thinking about it. Delete is the bin. Cancel is Escape, and it must leave the mark exactly as
/// it was: an Escape that quietly saves is worse than one that does nothing, because the person pressing
/// it believes they have undone something.
/// </summary>
public sealed partial class MarkLabelEditor : UserControl
{
    private bool _answered;

    public MarkLabelEditor()
    {
        InitializeComponent();

        Field.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Windows.System.VirtualKey.Enter:
                    e.Handled = true;
                    Answer(() => Committed?.Invoke(Field.Text.Trim()));
                    break;

                case Windows.System.VirtualKey.Escape:
                    e.Handled = true;
                    Answer(() => Cancelled?.Invoke());
                    break;
            }
        };
    }

    /// <summary>The typed words, from the tick or from Enter.</summary>
    public event Action<string>? Committed;

    /// <summary>The bin.</summary>
    public event Action? Deleted;

    /// <summary>Escape, or the host taking the editor away. The mark is left alone.</summary>
    public event Action? Cancelled;

    /// <summary>
    /// Show the mark's current words, ready to be typed over.
    ///
    /// Selected rather than with the caret at the end: relabelling is usually replacing, and a person
    /// who wants to append presses End — which is one key, where clearing a selected-nothing field is a
    /// drag or a held backspace.
    /// </summary>
    public void Open(string current)
    {
        _answered = false;
        Field.Text = current ?? string.Empty;
        Field.Focus(FocusState.Programmatic);
        Field.SelectAll();
    }

    /// <summary>Where on the canvas this sits. The host knows; this does not.</summary>
    public void PlaceAt(double x, double y) => Margin = new Thickness(x, y, 0, 0);

    private void OnDone(object sender, RoutedEventArgs e) => Answer(() => Committed?.Invoke(Field.Text.Trim()));

    private void OnDelete(object sender, RoutedEventArgs e) => Answer(() => Deleted?.Invoke());

    /// <summary>
    /// One answer per opening.
    ///
    /// Enter moves focus, focus loss can close the editor, and closing can cancel — so without this,
    /// pressing Enter commits and then immediately cancels the thing it just committed.
    /// </summary>
    private void Answer(Action what)
    {
        if (_answered) return;
        _answered = true;
        what();
    }
}
