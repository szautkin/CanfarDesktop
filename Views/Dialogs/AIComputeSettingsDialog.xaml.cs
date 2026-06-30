using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CanfarDesktop.Views.Dialogs;

/// <summary>Thin standalone host for <see cref="AIComputeSettingsPanel"/> (the panel self-resolves its
/// service from DI and persists itself).</summary>
public sealed partial class AIComputeSettingsDialog : ContentDialog
{
    public AIComputeSettingsDialog() => InitializeComponent();

    public static Task ShowAsync(XamlRoot root)
        => new AIComputeSettingsDialog { XamlRoot = root }.ShowAsync().AsTask();
}
