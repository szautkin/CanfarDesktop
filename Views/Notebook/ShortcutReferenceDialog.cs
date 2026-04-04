namespace CanfarDesktop.Views.Notebook;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

/// <summary>
/// Shows a keyboard shortcuts reference as a ContentDialog.
/// </summary>
public static class ShortcutReferenceDialog
{
    public static async Task ShowAsync(XamlRoot xamlRoot)
    {
        var content = new ScrollViewer
        {
            MaxHeight = 500,
            Content = BuildContent(),
        };

        var dialog = new ContentDialog
        {
            Title = "Keyboard Shortcuts",
            Content = content,
            CloseButtonText = "Close",
            XamlRoot = xamlRoot,
        };

        await dialog.ShowAsync();
    }

    private static StackPanel BuildContent()
    {
        var panel = new StackPanel { Spacing = 16 };

        panel.Children.Add(BuildSection("File", new[]
        {
            ("Ctrl+N", "New notebook tab"),
            ("Ctrl+O", "Open notebook"),
            ("Ctrl+S", "Save"),
            ("Ctrl+Shift+S", "Save As"),
            ("Ctrl+W", "Close tab"),
        }));

        panel.Children.Add(BuildSection("Execution", new[]
        {
            ("Ctrl+Enter", "Run cell"),
            ("Shift+Enter", "Run cell and advance"),
            ("Ctrl+Shift+Enter", "Run all cells"),
            ("I, I", "Interrupt kernel"),
            ("0, 0", "Restart kernel"),
        }));

        panel.Children.Add(BuildSection("Cell Operations (Command Mode)", new[]
        {
            ("A", "Add cell above"),
            ("B", "Add cell below"),
            ("D, D", "Delete cell"),
            ("Y", "Change to code cell"),
            ("M", "Change to markdown cell"),
            ("O", "Toggle output collapse"),
        }));

        panel.Children.Add(BuildSection("Navigation (Command Mode)", new[]
        {
            ("Up / K", "Select previous cell"),
            ("Down / J", "Select next cell"),
            ("Enter", "Enter edit mode"),
            ("Escape", "Exit edit mode"),
        }));

        return panel;
    }

    private static StackPanel BuildSection(string title, (string key, string desc)[] shortcuts)
    {
        var section = new StackPanel { Spacing = 4 };
        section.Children.Add(new TextBlock
        {
            Text = title,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        });

        foreach (var (key, desc) in shortcuts)
        {
            var row = new Grid { ColumnDefinitions = { new() { Width = new GridLength(160) }, new() { Width = new GridLength(1, GridUnitType.Star) } } };
            row.Children.Add(new TextBlock
            {
                Text = key,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
            });
            var descTb = new TextBlock
            {
                Text = desc,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            };
            Grid.SetColumn(descTb, 1);
            row.Children.Add(descTb);
            section.Children.Add(row);
        }

        return section;
    }
}
