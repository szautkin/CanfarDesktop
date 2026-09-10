using Microsoft.UI.Xaml;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Views.Controls;

/// <summary>
/// Wires a <see cref="MarksPanel"/> to a <see cref="MarkEditor"/>.
///
/// Nothing but wiring: what each control MEANS is decided in the editor, once, so that a viewer cannot
/// give its panel a different meaning by accident. This exists as its own class because it is the only
/// part that has to touch a UI framework, and keeping it out of the editor is what lets every rule about
/// marks be tested without a window.
/// </summary>
public sealed class MarkPanelBinding
{
    private readonly MarksPanel _panel;
    private readonly MarkEditor _editor;

    private MarkPanelBinding(MarksPanel panel, MarkEditor editor)
    {
        _panel = panel;
        _editor = editor;

        panel.DrawArmedChanged += editor.SetDrawArmed;
        panel.KindChanged += editor.SetKind;
        panel.StyleChanged += editor.ApplyStyle;
        panel.SelectionChanged += editor.Select;
        panel.EditRequested += id => { editor.Select(id); editor.BeginLabelEdit(id); };
        panel.DeleteRequested += editor.Delete;
        panel.ClearAllRequested += editor.ClearAll;

        // While the row is on screen it is the authority on what the next mark looks like; the stored
        // preference takes over when it is not.
        editor.StyleSource = () => panel.Visibility == Visibility.Visible ? panel.Style() : null;

        editor.Changed += Show;
    }

    /// <summary>
    /// Connect a panel to an editor, once.
    ///
    /// Once because the panel's events outlive any one file: re-subscribing each time the section opened
    /// would fire every handler as many times as it had been opened, so deleting one mark would delete
    /// several.
    /// </summary>
    public static MarkPanelBinding Attach(MarksPanel panel, MarkEditor editor) => new(panel, editor);

    /// <summary>Show the panel what there is. Cheap enough to call after any change.</summary>
    public void Show()
    {
        if (_panel.Visibility != Visibility.Visible) return;

        _panel.Show(_editor.Marks, _editor.SelectedId);
        _panel.Kind = _editor.Kind;
        _panel.DrawArmed = _editor.DrawArmed;
    }
}
