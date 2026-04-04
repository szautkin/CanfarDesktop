using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CanfarDesktop.Helpers;

/// <summary>
/// Shared UI element factory. Eliminates DRY violations between SearchPage and ResearchPage.
/// </summary>
public static class UIFactory
{
    /// <summary>
    /// Create a label + value row for metadata display.
    /// Returns null if value is empty (caller should skip).
    /// </summary>
    public static StackPanel? CreateMetadataRow(string label, string value, double labelWidth = 150)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var fg = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Width = labelWidth,
            FontSize = 12,
            Foreground = fg
        });
        row.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 12,
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
            Foreground = fg
        });
        return row;
    }

    /// <summary>
    /// Create a button with icon + text label.
    /// </summary>
    public static Button CreateIconButton(string glyph, string label, RoutedEventHandler onClick)
    {
        var btn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = glyph, FontSize = 14 },
                    new TextBlock { Text = label }
                }
            }
        };
        btn.Click += onClick;
        return btn;
    }
}
