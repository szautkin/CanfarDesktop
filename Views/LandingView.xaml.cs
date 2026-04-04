using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace CanfarDesktop.Views;

public sealed partial class LandingView : UserControl
{
    public event EventHandler? PortalRequested;
    public event EventHandler? SearchRequested;
    public event EventHandler? ResearchRequested;

    public string StatusMessage
    {
        get => StatusText.Text;
        set => StatusText.Text = value;
    }

    public LandingView()
    {
        InitializeComponent();
    }

    private void OnPortalTapped(object sender, TappedRoutedEventArgs e)
    {
        PortalRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnSearchTapped(object sender, TappedRoutedEventArgs e)
    {
        SearchRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnResearchTapped(object sender, TappedRoutedEventArgs e)
    {
        ResearchRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnTilePointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
            border.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
    }

    private void OnTilePointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
            border.Background = null;
    }
}
