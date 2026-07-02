using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CanfarDesktop.Views.Dialogs;

/// <summary>
/// First-run Welcome card (macOS <c>WelcomeSheet</c> parity): one dismissible dialog — explicitly
/// NOT a multi-step tour — naming the breadth of the app, with a prominent path into the AI-agent
/// connect wizard and a low-key "explore on my own" dismiss. Presented once by MainWindow after
/// the Terms gate has been accepted; the caller stamps
/// <see cref="Helpers.WelcomePreferences"/> on dismiss so it never reappears for this version.
/// </summary>
public sealed partial class WelcomeDialog : ContentDialog
{
    public WelcomeDialog()
    {
        InitializeComponent();
    }

    /// <summary>Shows the Welcome card; returns true when the user chose "Set up the AI assistant".</summary>
    public static async Task<bool> ShowAsync(XamlRoot root)
        => await new WelcomeDialog { XamlRoot = root }.ShowAsync() == ContentDialogResult.Primary;
}
