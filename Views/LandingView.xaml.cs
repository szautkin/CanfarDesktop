using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace CanfarDesktop.Views;

public sealed partial class LandingView : UserControl
{
    public event EventHandler? PortalRequested;
    public event EventHandler? SearchRequested;
    public event EventHandler? ResearchRequested;
    public event EventHandler? StorageRequested;
    public event EventHandler? NotebookRequested;

    public string StatusMessage
    {
        get => StatusText.Text;
        set => StatusText.Text = value;
    }

    public class TileData
    {
        public string Title { get; set; } = "";
        public string Glyph { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string Key { get; set; } = "";
    }

    public List<TileData> Tiles { get; } =
    [
        new() { Title = "Portal", Glyph = "\uE7F4", Subtitle = "Manage sessions & data", Key = "portal" },
        new() { Title = "Search", Glyph = "\uE721", Subtitle = "Explore the CADC archive", Key = "search" },
        new() { Title = "Research", Glyph = "\uE8B7", Subtitle = "Downloaded observations", Key = "research" },
        new() { Title = "Storage", Glyph = "\uEDA2", Subtitle = "Browse VOSpace files", Key = "storage" },
        new() { Title = "Notebook", Glyph = "\uE70B", Subtitle = "Open & run .ipynb files", Key = "notebook" },
    ];

    public LandingView()
    {
        InitializeComponent();
        TilesRepeater.ItemsSource = Tiles;
    }

    private void OnTileTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string key)
        {
            switch (key)
            {
                case "portal": PortalRequested?.Invoke(this, EventArgs.Empty); break;
                case "search": SearchRequested?.Invoke(this, EventArgs.Empty); break;
                case "research": ResearchRequested?.Invoke(this, EventArgs.Empty); break;
                case "storage": StorageRequested?.Invoke(this, EventArgs.Empty); break;
                case "notebook": NotebookRequested?.Invoke(this, EventArgs.Empty); break;
            }
        }
    }

    private void OnTilePointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is Border border)
            border.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
    }

    private void OnTilePointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is Border border)
            border.Background = null;
    }
}
