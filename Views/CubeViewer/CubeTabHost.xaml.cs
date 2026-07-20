using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;
using CanfarDesktop.Services.CubeViewer;
using static CanfarDesktop.Views.WindowHelper;

namespace CanfarDesktop.Views.CubeViewer;

/// <summary>
/// Tabbed host for the 3D Cube Viewer: each tab is a self-contained <see cref="CubeViewerPage"/>
/// (its own GPU renderer + controls). Mirrors the FITS viewer's tabbed approach — open cubes into
/// new tabs, close them with the per-tab ✕, and an empty-state prompt when none are open. A WinUI
/// TabView reparents non-selected tab content OUT of the tree, so inactive tabs raise Unloaded →
/// PauseRendering and stop their render loop; only the selected tab ever drives a frame.
/// </summary>
public sealed partial class CubeTabHost : UserControl
{
    // Recently opened cubes, persisted across sessions and surfaced in the empty state.
    private readonly RecentCubesService _recents = new();

    public CubeTabHost()
    {
        InitializeComponent();
        UpdateEmptyState();
    }

    /// <summary>The <see cref="CubeViewerPage"/> in the active tab (for MCP / external control), or null.</summary>
    public CubeViewerPage? ActivePage =>
        (TabViewControl.SelectedItem as TabViewItem)?.Content as CubeViewerPage;

    /// <summary>
    /// Add a tab for a cube file, load it, and return the page. The returned task completes once the
    /// load finishes (the tab header updates to the cube's object/name on success).
    /// </summary>
    public async Task<CubeViewerPage> AddTabForFileAsync(string filePath)
    {
        var page = new CubeViewerPage();
        var tab = new TabViewItem
        {
            Content = page,
            Header = System.IO.Path.GetFileNameWithoutExtension(filePath),
            IconSource = new FontIconSource { Glyph = "" },
            Tag = page,
        };
        TabViewControl.TabItems.Add(tab);
        TabViewControl.SelectedItem = tab;
        UpdateEmptyState();

        bool ok = await page.LoadCubeAsync(filePath);
        if (ok)
        {
            var st = page.GetCubeState();
            tab.Header = !string.IsNullOrEmpty(st.Object) ? st.Object : st.Name;
            // Every successful open (picker, empty-state, recents, MCP) lands here — record it once.
            _recents.AddOrUpdate(filePath, tab.Header as string);
        }
        return page;
    }

    private async void OnAddTab(TabView sender, object args) => await PromptOpenAsync();
    private async void OnOpenFileEmpty(object sender, RoutedEventArgs e) => await PromptOpenAsync();

    private async Task PromptOpenAsync()
    {
        var hwnd = ActiveWindows.Count > 0 ? WindowNative.GetWindowHandle(ActiveWindows[0]) : nint.Zero;
        if (hwnd == nint.Zero) return;

        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add(".fits");
        picker.FileTypeFilter.Add(".fit");
        picker.FileTypeFilter.Add(".fts");
        var file = await picker.PickSingleFileAsync();
        if (file is not null) await AddTabForFileAsync(file.Path);
    }

    private void OnTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args) => CloseTabItem(args.Tab);

    /// <summary>Close the active cube tab (the MCP close_active_tab tool). False when none is open.</summary>
    public bool CloseActiveTab()
    {
        if (TabViewControl.SelectedItem is not TabViewItem tab) return false;
        CloseTabItem(tab);
        return true;
    }

    /// <summary>Number of open cube tabs.</summary>
    public int OpenTabCount => TabViewControl.TabItems.Count;

    /// <summary>Every open cube tab's index/name/active flag (the MCP list_open_tabs detail).</summary>
    public IReadOnlyList<(int Index, string Name, bool Active)> TabInfos()
    {
        var infos = new List<(int, string, bool)>(TabViewControl.TabItems.Count);
        for (int i = 0; i < TabViewControl.TabItems.Count; i++)
        {
            var tab = TabViewControl.TabItems[i] as TabViewItem;
            infos.Add((i, tab?.Header as string ?? "", ReferenceEquals(tab, TabViewControl.SelectedItem)));
        }
        return infos;
    }

    /// <summary>Make the tab at a 0-based index active (the MCP switch_cube_tab tool).</summary>
    public bool SwitchToTab(int index)
    {
        if (index < 0 || index >= TabViewControl.TabItems.Count) return false;
        TabViewControl.SelectedItem = TabViewControl.TabItems[index];
        return true;
    }

    /// <summary>The persisted recently-opened cubes (the MCP list_recent_cubes tool).</summary>
    public IReadOnlyList<RecentCubeEntry> RecentCubes => _recents.Entries;

    private void CloseTabItem(TabViewItem tab)
    {
        if (tab.Content is CubeViewerPage page) page.CleanupForClose();
        TabViewControl.TabItems.Remove(tab);
        UpdateEmptyState();
    }

    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // ActivePage is computed from the selection; nothing to push here.
    }

    private void UpdateEmptyState()
    {
        bool empty = TabViewControl.TabItems.Count == 0;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (empty) RefreshRecents();
    }

    /// <summary>Rebuild the empty-state recents list (a handful of entries — rebuilding is cheap).</summary>
    private void RefreshRecents()
    {
        RecentsList.Items.Clear();
        var entries = _recents.Entries;
        RecentsPanel.Visibility = entries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var entry in entries)
        {
            var btn = new HyperlinkButton
            {
                Content = entry.Name,
                Tag = entry.Path,
                Padding = new Thickness(8, 2, 8, 2),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            ToolTipService.SetToolTip(btn, entry.Path);
            btn.Click += OnRecentClick;
            RecentsList.Items.Add(btn);
        }
    }

    private async void OnRecentClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string path) return;
        // A file deleted since it was recorded silently drops out of the list instead of
        // opening a tab that can only fail.
        if (!File.Exists(path))
        {
            _recents.Remove(path);
            RefreshRecents();
            return;
        }
        await AddTabForFileAsync(path);
    }
}
