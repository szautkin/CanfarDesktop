namespace CanfarDesktop.Views.Notebook;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.Services.Notebook;

/// <summary>
/// Settings dialog for notebook configuration. Shows as a ContentDialog.
/// </summary>
public static class NotebookSettingsDialog
{
    public static async Task ShowAsync(XamlRoot xamlRoot)
    {
        var settings = App.Services.GetRequiredService<NotebookSettings>();

        var fontSizeBox = new ComboBox
        {
            Header = "Font size",
            Items = { 11, 12, 13, 14, 15, 16, 18, 20 },
            SelectedItem = settings.FontSize,
            MinWidth = 120,
        };

        var tabSizeBox = new ComboBox
        {
            Header = "Tab size (spaces)",
            Items = { 2, 4, 8 },
            SelectedItem = settings.TabSize,
            MinWidth = 120,
        };

        var wordWrapToggle = new ToggleSwitch
        {
            Header = "Word wrap",
            IsOn = settings.WordWrap,
        };

        var autosaveToggle = new ToggleSwitch
        {
            Header = "Autosave enabled",
            IsOn = settings.AutosaveEnabled,
        };

        var autosaveIntervalBox = new ComboBox
        {
            Header = "Autosave interval",
            Items = { "15 seconds", "30 seconds", "60 seconds", "120 seconds" },
            SelectedIndex = settings.AutosaveIntervalSeconds switch
            {
                15 => 0, 30 => 1, 60 => 2, 120 => 3, _ => 1
            },
            MinWidth = 160,
        };

        var timeoutBox = new ComboBox
        {
            Header = "Execution timeout warning",
            Items = { "30 seconds", "60 seconds", "120 seconds", "300 seconds", "Never" },
            SelectedIndex = settings.ExecutionTimeoutSeconds switch
            {
                30 => 0, 60 => 1, 120 => 2, 300 => 3, 0 => 4, _ => 1
            },
            MinWidth = 160,
        };

        var pythonPathBox = new TextBox
        {
            Header = "Python path (leave empty for auto-detect)",
            Text = settings.PythonPath ?? "",
            PlaceholderText = "Auto-detect from PATH",
            MinWidth = 300,
        };

        var toolbarToggle = new ToggleSwitch
        {
            Header = "Show toolbar",
            IsOn = settings.ShowToolbar,
        };

        // Log folder button
        var logButton = new HyperlinkButton
        {
            Content = "Open log folder",
        };
        logButton.Click += (_, _) => NotebookLogger.OpenLogFolder();

        var panel = new StackPanel { Spacing = 16, MinWidth = 360 };

        panel.Children.Add(new TextBlock { Text = "Editor", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
        var editorRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        editorRow.Children.Add(fontSizeBox);
        editorRow.Children.Add(tabSizeBox);
        panel.Children.Add(editorRow);
        panel.Children.Add(wordWrapToggle);

        panel.Children.Add(new TextBlock { Text = "Saving", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], Margin = new Thickness(0, 8, 0, 0) });
        panel.Children.Add(autosaveToggle);
        panel.Children.Add(autosaveIntervalBox);

        panel.Children.Add(new TextBlock { Text = "Execution", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], Margin = new Thickness(0, 8, 0, 0) });
        panel.Children.Add(timeoutBox);
        panel.Children.Add(pythonPathBox);

        panel.Children.Add(new TextBlock { Text = "Interface", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], Margin = new Thickness(0, 8, 0, 0) });
        panel.Children.Add(toolbarToggle);

        panel.Children.Add(new TextBlock { Text = "Diagnostics", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], Margin = new Thickness(0, 8, 0, 0) });
        panel.Children.Add(logButton);

        var dialog = new ContentDialog
        {
            Title = "Notebook Settings",
            Content = new ScrollViewer { Content = panel, MaxHeight = 500 },
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = xamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            settings.FontSize = fontSizeBox.SelectedItem is int fs ? fs : 13;
            settings.TabSize = tabSizeBox.SelectedItem is int ts ? ts : 4;
            settings.WordWrap = wordWrapToggle.IsOn;
            settings.AutosaveEnabled = autosaveToggle.IsOn;
            settings.AutosaveIntervalSeconds = autosaveIntervalBox.SelectedIndex switch
            {
                0 => 15, 1 => 30, 2 => 60, 3 => 120, _ => 30
            };
            settings.ExecutionTimeoutSeconds = timeoutBox.SelectedIndex switch
            {
                0 => 30, 1 => 60, 2 => 120, 3 => 300, 4 => 0, _ => 60
            };
            settings.PythonPath = string.IsNullOrWhiteSpace(pythonPathBox.Text) ? null : pythonPathBox.Text;
            settings.ShowToolbar = toolbarToggle.IsOn;
            settings.Save();
        }
    }
}
