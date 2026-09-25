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
    public event EventHandler? WorkflowsRequested;
    public event EventHandler? RemoteComputeRequested;
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
        public bool RequiresAuth => Helpers.AccountScreens.Contains(Key);

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

    // The account's own screens first (Portal, Remote Compute, Storage), then the archive, the viewers,
    // the notebook and workflows; AI Guide and AI Assistant are added last, in the constructor.
    //
    // The account screens need the CADC token: locked (badge + dim + tooltip) until sign-in — see
    // AccountScreens. Tapping still fires the event: MainWindow shows the login dialog and continues
    // to the chosen destination on success. Remote Compute shows whether or not it is set up: until
    // it is, the screen says what it takes, which is the point.
    public List<TileData> Tiles { get; } =
    [
        new() { Title = Helpers.Loc.T("Tile_Portal_Title"), Glyph = "\uE7F4", Subtitle = Helpers.Loc.T("Tile_Portal_Subtitle"), Key = "portal" },
        new() { Title = Helpers.Loc.T("Tile_RemoteCompute_Title"), Glyph = "\uE756", Subtitle = Helpers.Loc.T("Tile_RemoteCompute_Subtitle"), Key = "remoteCompute" },
        new() { Title = Helpers.Loc.T("Tile_Storage_Title"), Glyph = "\uEDA2", Subtitle = Helpers.Loc.T("Tile_Storage_Subtitle"), Key = "storage" },
        new() { Title = Helpers.Loc.T("Tile_Search_Title"), Glyph = "\uE721", Subtitle = Helpers.Loc.T("Tile_Search_Subtitle"), Key = "search" },
        new() { Title = Helpers.Loc.T("Tile_Research_Title"), Glyph = "\uE8B7", Subtitle = Helpers.Loc.T("Tile_Research_Subtitle"), Key = "research" },
        new() { Title = Helpers.Loc.T("Tile_Fits_Title"), Glyph = "\uE7B8", Subtitle = Helpers.Loc.T("Tile_Fits_Subtitle"), Key = "fits" },
        new() { Title = Helpers.Loc.T("Tile_Cube_Title"), Glyph = "\uE809", Subtitle = Helpers.Loc.T("Tile_Cube_Subtitle"), Key = "cube" },
        new() { Title = Helpers.Loc.T("Tile_Notebook_Title"), Glyph = "\uE70B", Subtitle = Helpers.Loc.T("Tile_Notebook_Subtitle"), Key = "notebook" },
        new() { Title = Helpers.Loc.T("Tile_Workflows_Title"), Glyph = "\uE9D5", Subtitle = Helpers.Loc.T("Tile_Workflows_Subtitle"), Key = "workflows" },
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

        // Locked until MainWindow says somebody is signed in.
        SetAuthenticated(false);
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

        // Adaptive launchpad: let wide windows spread the tiles over more columns (two rows on a
        // full-screen 1440p/4K display) while narrow windows keep the classic 3-wide grid.
        SizeChanged += (_, e) => UpdateTileColumns(e.NewSize.Width);
    }

    private void UpdateTileColumns(double availableWidth)
    {
        const double tile = 190, spacing = 20;
        var fit = (int)Math.Floor((availableWidth + spacing) / (tile + spacing));
        var columns = Math.Clamp(fit, 3, 5);
        if (TilesLayout.MaximumRowsOrColumns != columns)
            TilesLayout.MaximumRowsOrColumns = columns;
    }

    /// <summary>Locks/unlocks the account tiles (Portal, Remote Compute, Storage). Called by MainWindow on auth changes.</summary>
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
                case "workflows": WorkflowsRequested?.Invoke(this, EventArgs.Empty); break;
                case "remoteCompute": RemoteComputeRequested?.Invoke(this, EventArgs.Empty); break;
                case "aiAssistant": AiAssistantRequested?.Invoke(this, EventArgs.Empty); break;
            }
        }
    }
}
