namespace CanfarDesktop.Helpers.Notebook;

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

/// <summary>
/// Shared theme utility for notebook cell controls. Avoids duplicating
/// brush lookup logic across CodeCellControl and MarkdownCellControl.
/// </summary>
public static class ThemeHelper
{
    public static Brush GetBrush(string key)
    {
        if (Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush)
            return brush;
        return new SolidColorBrush(Colors.Gray);
    }

    public static Brush AccentBrush => GetBrush("AccentFillColorDefaultBrush");
    public static Brush SuccessBrush => GetBrush("SystemFillColorSuccessBrush");
    public static Brush Transparent => new SolidColorBrush(Colors.Transparent);
}
