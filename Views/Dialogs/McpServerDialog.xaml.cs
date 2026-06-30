using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CanfarDesktop.Views.Dialogs;

/// <summary>Thin standalone host for <see cref="McpServerSettingsPanel"/> (the panel self-resolves its
/// services from DI and persists live).</summary>
public sealed partial class McpServerDialog : ContentDialog
{
    public McpServerDialog() => InitializeComponent();

    public static Task ShowAsync(XamlRoot root)
        => new McpServerDialog { XamlRoot = root }.ShowAsync().AsTask();
}
