using System.ComponentModel;
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
    public event EventHandler? AiAssistantRequested;

    public string StatusMessage
    {
        get => StatusText.Text;
        set => StatusText.Text = value;
    }

    public class TileData : INotifyPropertyChanged
    {
        public string Title { get; set; } = "";
        public string Glyph { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string Key { get; set; } = "";
        public bool RequiresAuth { get; set; }

        // Auth-gated lock state. Mutated by SetAuthenticated after the tiles are
        // realized, so the derived visual properties must raise change notifications.
        private bool _isLocked;
        public bool IsLocked
        {
            get => _isLocked;
            set
            {
                if (_isLocked == value) return;
                _isLocked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LockVisibility)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TileOpacity)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ToolTip)));
            }
        }

        public Visibility LockVisibility => _isLocked ? Visibility.Visible : Visibility.Collapsed;
        public double TileOpacity => _isLocked ? 0.7 : 1.0;
        public string ToolTip => _isLocked ? Helpers.Loc.T("Landing_SignInToAccess") : Subtitle;

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public List<TileData> Tiles { get; } =
    [
        // Portal + Storage need the CADC token: locked (badge + dim + tooltip) until
        // sign-in. Tapping still fires the event: MainWindow shows the login dialog
        // and continues to the chosen destination on success.
        new() { Title = Helpers.Loc.T("Tile_Portal_Title"), Glyph = "\uE7F4", Subtitle = Helpers.Loc.T("Tile_Portal_Subtitle"), Key = "portal", RequiresAuth = true, IsLocked = true },
        new() { Title = Helpers.Loc.T("Tile_Search_Title"), Glyph = "\uE721", Subtitle = Helpers.Loc.T("Tile_Search_Subtitle"), Key = "search" },
        new() { Title = Helpers.Loc.T("Tile_Research_Title"), Glyph = "\uE8B7", Subtitle = Helpers.Loc.T("Tile_Research_Subtitle"), Key = "research" },
        new() { Title = Helpers.Loc.T("Tile_Storage_Title"), Glyph = "\uEDA2", Subtitle = Helpers.Loc.T("Tile_Storage_Subtitle"), Key = "storage", RequiresAuth = true, IsLocked = true },
        new() { Title = Helpers.Loc.T("Tile_Notebook_Title"), Glyph = "\uE70B", Subtitle = Helpers.Loc.T("Tile_Notebook_Subtitle"), Key = "notebook" },
        new() { Title = Helpers.Loc.T("Tile_Fits_Title"), Glyph = "\uE7B8", Subtitle = Helpers.Loc.T("Tile_Fits_Subtitle"), Key = "fits" },
        new() { Title = Helpers.Loc.T("Tile_Cube_Title"), Glyph = "\uE809", Subtitle = Helpers.Loc.T("Tile_Cube_Subtitle"), Key = "cube" },
    ];

    public LandingView()
    {
        InitializeComponent();

        // The AI Guide tile is opt-in (hidden by default; toggle in Settings ▸ MCP server). The saved
        // overrides + guide tools stay active regardless — this only controls the launchpad shortcut.
        if (ReadShowAiGuideTile())
            Tiles.Add(new() { Title = Helpers.Loc.T("Tile_AiGuide_Title"), Glyph = "", Subtitle = Helpers.Loc.T("Tile_AiGuide_Subtitle"), Key = "aiGuide" });

        // AI Assistant — newcomer entry point to the connect wizard; always shown (macOS parity).
        Tiles.Add(new() { Title = Helpers.Loc.T("Tile_AiAssistant_Title"), Glyph = "", Subtitle = Helpers.Loc.T("Tile_AiAssistant_Subtitle"), Key = "aiAssistant" });

        TilesRepeater.ItemsSource = Tiles;

        // Subtle hover scale on each tile (reduce-motion aware) — the landing's share of the app
        // motion vocabulary, matching the macOS tile hover. Tracked in a set because Tag carries
        // the tile key for OnTileClicked and ElementPrepared can re-fire for the same element.
        var hooked = new HashSet<Microsoft.UI.Xaml.UIElement>();
        TilesRepeater.ElementPrepared += (_, args) =>
        {
            if (args.Element is Microsoft.UI.Xaml.FrameworkElement tile && hooked.Add(tile))
                Helpers.AppMotion.AttachHoverScale(tile);
        };
    }

    /// <summary>Locks/unlocks the auth-gated tiles (Portal, Storage). Called by MainWindow on auth changes.</summary>
    public void SetAuthenticated(bool authenticated)
    {
        foreach (var tile in Tiles)
        {
            if (tile.RequiresAuth)
                tile.IsLocked = !authenticated;
        }
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
                case "aiAssistant": AiAssistantRequested?.Invoke(this, EventArgs.Empty); break;
            }
        }
    }
}
