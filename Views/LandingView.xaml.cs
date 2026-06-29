using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using CanfarDesktop.Services.AiGuide;

namespace CanfarDesktop.Views;

public sealed partial class LandingView : UserControl
{
    public event EventHandler? PortalRequested;
    public event EventHandler? SearchRequested;
    public event EventHandler? ResearchRequested;
    public event EventHandler? StorageRequested;
    public event EventHandler? NotebookRequested;
    public event EventHandler? FitsViewerRequested;
    public event EventHandler? CubeViewerRequested;
    public event EventHandler? AiGuideRequested;

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
        new() { Title = "FITS Viewer", Glyph = "\uE7B8", Subtitle = "View astronomical images", Key = "fits" },
        new() { Title = "Cube Viewer", Glyph = "\uE809", Subtitle = "3D volume ray-marcher", Key = "cube" },
    ];

    public LandingView()
    {
        InitializeComponent();

        // The AI Guide tile is opt-in (hidden by default; toggle in Settings ▸ MCP server). The saved
        // overrides + guide tools stay active regardless — this only controls the launchpad shortcut.
        if (ReadShowAiGuideTile())
            Tiles.Add(new() { Title = "AI Guide", Glyph = "", Subtitle = "Tune the agent's tools", Key = "aiGuide" });

        TilesRepeater.ItemsSource = Tiles;
    }

    private static bool ReadShowAiGuideTile()
    {
        // Default ON: the tile shows unless the user has explicitly turned it off in Settings ▸ MCP server.
        try
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            return !values.TryGetValue(AiGuidePreferences.ShowLandingTileKey, out var v) || v is not bool b || b;
        }
        catch { return true; }
    }

    private void OnTileClicked(object sender, RoutedEventArgs e)
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
                case "fits": FitsViewerRequested?.Invoke(this, EventArgs.Empty); break;
                case "cube": CubeViewerRequested?.Invoke(this, EventArgs.Empty); break;
                case "aiGuide": AiGuideRequested?.Invoke(this, EventArgs.Empty); break;
            }
        }
    }
}
