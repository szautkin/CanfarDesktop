using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace CanfarDesktop.Helpers;

public static class VisualTree
{
    /// <summary>Depth-first search for the first descendant of the given type.</summary>
    public static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } nested) return nested;
        }
        return null;
    }
}
