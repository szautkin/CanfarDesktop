using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;
using static CanfarDesktop.Views.WindowHelper;

namespace CanfarDesktop.Views.CubeViewer;

/// <summary>
/// Tabbed host for the 3D Cube Viewer: each tab is a self-contained <see cref="CubeViewerPage"/>
/// (its own GPU renderer + controls). Mirrors the FITS viewer's tabbed approach — open cubes into
/// new tabs, close them with the per-tab ✕, and an empty-state prompt when none are open. Only the
/// active tab renders (inactive pages collapse, pausing their render loops).
/// </summary>
public sealed partial class CubeTabHost : UserControl
{
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

    private void OnTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Tab.Content is CubeViewerPage page) page.CleanupForClose();
        sender.TabItems.Remove(args.Tab);
        UpdateEmptyState();
    }

    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // ActivePage is computed from the selection; nothing to push here.
    }

    private void UpdateEmptyState()
        => EmptyState.Visibility = TabViewControl.TabItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
}
