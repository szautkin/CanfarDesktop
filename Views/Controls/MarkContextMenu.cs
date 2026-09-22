using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Views.Controls;

/// <summary>
/// A mark's menu, built once and shown from wherever a person points at a mark — the FITS canvas, the
/// cube canvas, or a row of the marks panel.
///
/// <para>The entries and the rules about them are in <see cref="MarkCommands"/>, which knows nothing
/// about XAML; this turns that list into a flyout. Keeping the two apart is what lets the decision be
/// tested without a window, and it is the decision that has the rules in it.</para>
///
/// <para>Every item is localized, carries an automation name, and is reachable by keyboard: a flyout
/// takes arrow keys and Enter on its own, and the viewers open it on the Menu key as well as on a
/// right-click, so pointing at a mark never requires a mouse.</para>
/// </summary>
public static class MarkContextMenu
{
    /// <summary>
    /// Build the menu for one mark.
    ///
    /// <paramref name="invoke"/> is called with the command that was chosen; what it means is the
    /// viewer's business, and each viewer answers the same list differently.
    /// </summary>
    public static MenuFlyout Build(IReadOnlyList<MarkCommandItem> items, Action<MarkCommand> invoke)
    {
        var flyout = new MenuFlyout();
        var separatorPending = false;

        foreach (var item in items)
        {
            // The destructive command is set apart rather than sitting under the navigation ones,
            // where a slipped click lands on Delete instead of Centre.
            if (item.Destructive && !separatorPending && flyout.Items.Count > 0)
            {
                flyout.Items.Add(new MenuFlyoutSeparator());
                separatorPending = true;
            }

            var text = Loc.T(item.Uid);
            var entry = new MenuFlyoutItem
            {
                Text = text,
                IsEnabled = item.Enabled,
                Icon = new FontIcon { Glyph = item.Glyph },
            };

            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(entry, text);

            // A greyed item that does not say why reads as a broken menu.
            if (!item.Enabled && item.DisabledReasonUid is { } reason)
                ToolTipService.SetToolTip(entry, Loc.T(reason));

            var command = item.Command;
            entry.Click += (_, _) => invoke(command);

            flyout.Items.Add(entry);
        }

        return flyout;
    }

    /// <summary>
    /// Pick a mark out and show its menu — the whole gesture, for any viewer.
    ///
    /// <para>Selecting first is part of it: a menu acting on something that is not visibly chosen is
    /// how people delete the wrong thing. Every caller was doing exactly this, and one of them was
    /// doing it by hand.</para>
    /// </summary>
    public static void ShowFor(
        IMarkCommandHost host, string id, FrameworkElement target, double x, double y)
    {
        var menu = Build(MarkCommands.For(host.CommandContextFor(id)), c => host.InvokeMarkCommand(c, id));
        ShowAt(menu, target, x, y);
    }

    /// <summary>
    /// Show the menu at a point in <paramref name="target"/>'s own coordinates — where the pointer was,
    /// so the menu appears under the hand that asked for it.
    /// </summary>
    public static void ShowAt(MenuFlyout flyout, FrameworkElement target, double x, double y)
        => flyout.ShowAt(target, new FlyoutShowOptions
        {
            Position = new Windows.Foundation.Point(x, y),
            ShowMode = FlyoutShowMode.Standard,
        });
}
