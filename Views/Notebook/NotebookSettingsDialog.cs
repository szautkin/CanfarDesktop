namespace CanfarDesktop.Views.Notebook;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

/// <summary>Thin standalone host for <see cref="NotebookSettingsPanel"/> (which self-resolves its
/// settings from DI and auto-saves on change). Kept for the existing call sites.</summary>
public static class NotebookSettingsDialog
{
    public static async Task ShowAsync(XamlRoot xamlRoot)
    {
        var dialog = new ContentDialog
        {
            Title = "Notebook Settings",
            Content = new ScrollViewer { Content = new NotebookSettingsPanel(), MaxHeight = 500 },
            CloseButtonText = "Close",
            XamlRoot = xamlRoot,
        };
        await dialog.ShowAsync();
    }
}
