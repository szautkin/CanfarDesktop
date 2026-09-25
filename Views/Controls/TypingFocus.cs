using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace CanfarDesktop.Views.Controls;

/// <summary>
/// Whether the person is typing into something.
///
/// <para>A single-key shortcut and a text field want the same keystrokes. The cube viewer plays the
/// channel animation on Space and steps channels on the arrows — which is exactly what happened when
/// someone put a space in a mark's label, or tried to move the caret in the marks filter: the words
/// went nowhere and the cube started playing.</para>
///
/// <para>Here rather than in each viewer because it is one rule and there are already three places
/// that need it. The notebook has its own, stricter version: it decides whether focus is in the CELL
/// AREA, because it has destructive single-key commands and a comment recording what inferring them
/// from "not a TextBox" once cost. This is the weaker question — "is a text field taking these keys"
/// — which is all a play/pause accelerator needs to ask.</para>
/// </summary>
internal static class TypingFocus
{
    /// <summary>
    /// True when the focused element is a text field, or sits inside one.
    ///
    /// The walk up the tree matters: a templated control hands focus to a part of itself, so an
    /// AutoSuggestBox or an editable ComboBox reports its inner TextBox — or, on some templates, a
    /// child of it. Asking only about the focused element itself would miss those and let the
    /// shortcut through in the middle of a word.
    /// </summary>
    public static bool IsTyping(XamlRoot? root)
    {
        if (root is null) return false;
        if (FocusManager.GetFocusedElement(root) is not DependencyObject focused) return false;

        for (var current = focused; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is TextBox or RichEditBox or PasswordBox or AutoSuggestBox) return true;

            // An ordinary ComboBox is a list and should not swallow a shortcut; an editable one is a
            // text field wearing a list's clothes.
            if (current is ComboBox { IsEditable: true }) return true;
        }

        return false;
    }
}
