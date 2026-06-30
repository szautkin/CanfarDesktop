using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CanfarDesktop.Views.Dialogs;

/// <summary>Thin standalone host for <see cref="ImageDiscoverySettingsPanel"/> (the panel self-resolves
/// its services from DI and persists itself). Opened from the images dashboard card.</summary>
public sealed partial class ImageDiscoverySettingsDialog : ContentDialog
{
    public ImageDiscoverySettingsDialog() => InitializeComponent();

    public static Task ShowAsync(XamlRoot root)
        => new ImageDiscoverySettingsDialog { XamlRoot = root }.ShowAsync().AsTask();
}
