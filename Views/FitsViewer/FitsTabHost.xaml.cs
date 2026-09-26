using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models.Fits;
using CanfarDesktop.Services.Fits;
using CanfarDesktop.ViewModels;
using static CanfarDesktop.Views.WindowHelper;

namespace CanfarDesktop.Views.FitsViewer;

public sealed partial class FitsTabHost : UserControl
{
    public FitsTabHostViewModel ViewModel { get; }
    public event Action<double, double>? SearchAtPositionRequested;

    /// <summary>A mark's region to cut out of its observation's file in the archive, from any tab.</summary>
    public event Action<string, string?, Models.Cutouts.SkyRegion>? CutoutRequested;

    private void OnPageCutoutRequested(string publisherId, string? artifactId, Models.Cutouts.SkyRegion region)
        => CutoutRequested?.Invoke(publisherId, artifactId, region);
    public event Action? AllTabsClosed;

    private FitsViewerPage? _activePage;
    private bool _suppressToolbarSync;
    private bool _coordPanelVisible;
    private BlinkSession? _blinkSession;
    private DispatcherTimer? _blinkTimer;
    /// <summary>Image A's transform before blink reframed it — restored on Stop.</summary>
    private (FitsViewerPage page,
             (double rotation, double scaleX, double scaleY, double translateX, double translateY) transform)? _blinkRestore;

    public FitsTabHost(FitsTabHostViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        CoordListView.ItemsSource = ViewModel.SavedCoordinates;
        // Localized initial readout (code owns this text at runtime, so no x:Uid on it).
        PanelCrosshairText.Text = Loc.T("Fits_NoCrosshair");

        // Pre-select the 100% item (editable ComboBox ignores Text= on first render)
        Loaded += (_, _) =>
        {
            _suppressToolbarSync = true;
            ZoomPresetCombo.SelectedIndex = 3; // "100%" item
            _suppressToolbarSync = false;
            UpdateEmptyState();
        };
    }

    // ── Tab lifecycle ────────────────────────────────────────────────────────

    public FitsViewerPage AddNewTab()
    {
        var tabItem = ViewModel.AddNewTab();
        var page = CreateTabViewItem(tabItem);
        UpdateEmptyState();
        return page;
    }

    /// <summary>
    /// Open a file, or come back to it if it is already open.
    ///
    /// <para>Opening the same path twice used to give two identical tabs. That is wrong on its own —
    /// nobody means "another copy" by opening a file they already have open — and it turned a
    /// harmless retry into damage: open_fits_file on a large multi-extension file takes longer than
    /// the MCP dispatch will wait, so an agent is told it failed when it did not, tries again, and
    /// ends up with duplicates.</para>
    /// </summary>
    public async Task<FitsViewerPage> AddTabForFileAsync(string filePath)
    {
        if (FindTabFor(filePath) is { } already)
        {
            SelectTab(already);
            return already;
        }

        var tabItem = ViewModel.AddNewTab();
        var page = CreateTabViewItem(tabItem);

        // Before the await, not after it. The parse of a multi-extension file takes seconds, and this
        // call used to sit below it — so for all of that time the tab existed, its progress panel was
        // up, and "No image open" was still painted over the top of it, offering a button to open the
        // file that was already opening. The cube viewer has always hidden its empty state here.
        UpdateEmptyState();

        await page.OpenFileAsync(filePath);
        SyncToolbarToActiveTab();
        UpdateWcsSyncWarning(); // WCS is loaded now — re-check if opening this file made sync approximate

        // Every route in — picker, empty state, recents, Research, Storage, MCP — lands here, so this
        // is the one place that has to remember it.
        _recents.AddOrUpdate(filePath, System.IO.Path.GetFileNameWithoutExtension(filePath));
        return page;
    }

    private record TabHandlers(
        System.ComponentModel.PropertyChangedEventHandler HeaderHandler,
        System.ComponentModel.PropertyChangedEventHandler LoadingHandler,
        Action<double, double> SearchHandler,
        Action<double> ZoomHandler);

    private readonly Dictionary<FitsViewerTabItem, TabHandlers> _tabHandlers = [];
    private readonly Dictionary<FitsViewerTabItem, FitsViewerPage> _tabPages = [];

    /// <summary>The file the tab on screen is showing, for the annotation tools. Null when none is.</summary>
    public string? ActiveAnnotationTarget => _activePage?.AnnotationTarget;

    /// <summary>The image the active tab's marks are on, for an export. Null when it shows another file.</summary>
    internal Helpers.MarkExport.Source? MarkExportSource(string target) => _activePage?.MarkExportSource(target);

    /// <summary>
    /// Redraw the active tab's marks after something changed them elsewhere. False when this viewer is
    /// not showing that file — the marks are stored against the file either way.
    /// </summary>
    public bool RefreshAnnotations(string target, string? selectId)
        => _activePage?.RefreshAnnotations(target, selectId) ?? false;

    public bool DeselectAnnotation(string target)
        => _activePage?.DeselectAnnotation(target) ?? false;

    /// <summary>Render a figure of the active tab, for <c>export_fits_figure</c>.</summary>
    public Task<CanfarDesktop.Mcp.Tools.Write.FitsFigureOutcome> ExportFigureAsync(
        CanfarDesktop.Mcp.Tools.Write.FitsFigureRequest request)
        => _activePage?.ExportFigureAsync(request)
           ?? Task.FromResult(CanfarDesktop.Mcp.Tools.Write.FitsFigureOutcome.Unavailable("no FITS image is open"));

    /// <summary>Capture what the active tab is showing, for <c>get_fits_image</c>.</summary>
    public Task<CanfarDesktop.Mcp.Tools.Write.ViewerCapture> CaptureAsync(
        CanfarDesktop.Mcp.Tools.Write.ViewerCaptureRequest request)
        => _activePage?.CaptureAsync(request)
           ?? Task.FromResult(CanfarDesktop.Mcp.Tools.Write.ViewerCapture.Unavailable("no FITS image is open"));

    private FitsViewerPage CreateTabViewItem(FitsViewerTabItem tabItem)
    {
        var page = new FitsViewerPage(tabItem.ViewModel);

        // The marks are keyed by the file, so every tab reads the same store; the page works out which
        // file is its own from its view model.
        page.AttachAnnotationStore(
            Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                .GetRequiredService<CanfarDesktop.Services.Fits.IAnnotationStore>(App.Services));

        // Store handlers so we can unsubscribe on tab close
        Action<double, double> searchHandler = (ra, dec) => SearchAtPositionRequested?.Invoke(ra, dec);
        page.SearchAtPositionRequested += searchHandler;

        // The mode turns itself off after one region, and the button has to follow it rather than
        // keep claiming to be on.
        page.SelectingAreaChanged += OnPageSelectingAreaChanged;
        page.CutoutRequested += OnPageCutoutRequested;

        var tab = new TabViewItem
        {
            Content = page,
            Header = tabItem.Header,
            IconSource = new SymbolIconSource { Symbol = Symbol.Document },
            Tag = tabItem,
        };

        System.ComponentModel.PropertyChangedEventHandler headerHandler = (_, e) =>
        {
            if (e.PropertyName == nameof(FitsViewerTabItem.Header))
                DispatcherQueue.TryEnqueue(() => tab.Header = tabItem.Header);
        };
        tabItem.PropertyChanged += headerHandler;

        System.ComponentModel.PropertyChangedEventHandler loadingHandler = (_, e) =>
        {
            if (tabItem != ViewModel.ActiveTab) return;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (e.PropertyName == nameof(FitsViewerViewModel.IsLoading))
                    LoadingRing.IsActive = tabItem.ViewModel.IsLoading;
                else if (e.PropertyName == nameof(FitsViewerViewModel.CrosshairPosition))
                {
                    UpdatePanelCrosshairText(tabItem.ViewModel);
                    // Write to shared state — other tabs read it when they become visible
                    if (ViewModel.IsLinkedCrosshairEnabled && tabItem.ViewModel.CrosshairPosition is { } pos)
                        ViewModel.LinkedCrosshairPosition = pos;
                }
            });
        };
        tabItem.ViewModel.PropertyChanged += loadingHandler;

        Action<double> zoomHandler = mag => DispatcherQueue.TryEnqueue(() =>
        {
            UpdateZoomDisplay(mag);
            if (ViewModel.IsSyncZoomEnabled && tabItem == ViewModel.ActiveTab)
                UpdateSharedAngularZoom();
        });
        page.ZoomChanged += zoomHandler;

        _tabHandlers[tabItem] = new TabHandlers(headerHandler, loadingHandler, searchHandler, zoomHandler);
        _tabPages[tabItem] = page;

        TabViewControl.TabItems.Add(tab);
        TabViewControl.SelectedItem = tab;
        return page;
    }

    private void OnAddTab(TabView sender, object args) => PromptOpenFile();

    private void OnTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args) => CloseTabItem(args.Tab);

    /// <summary>Close the active FITS tab (the MCP close_active_tab tool). False when none is open.</summary>
    public bool CloseActiveTab()
    {
        if (TabViewControl.SelectedItem is not TabViewItem tab) return false;
        CloseTabItem(tab);
        return true;
    }

    /// <summary>Number of open FITS tabs.</summary>
    public int OpenTabCount => TabViewControl.TabItems.Count;


    /// <summary>
    /// The open tab showing this file, if there is one.
    ///
    /// Compared as full paths, case-insensitively, because Windows treats them that way and the same
    /// file arrives here spelled differently depending on the route in — a picker, a recents entry, a
    /// downloaded observation's stored path, or an agent's argument.
    /// </summary>
    private FitsViewerPage? FindTabFor(string filePath)
    {
        string full;
        try { full = System.IO.Path.GetFullPath(filePath); }
        catch { return null; }

        foreach (var item in TabViewControl.TabItems)
        {
            if (item is not TabViewItem { Content: FitsViewerPage page }) continue;
            if (page.ViewModel.FilePath is not { Length: > 0 } open) continue;

            string openFull;
            try { openFull = System.IO.Path.GetFullPath(open); }
            catch { continue; }

            if (string.Equals(openFull, full, StringComparison.OrdinalIgnoreCase)) return page;
        }

        return null;
    }

    /// <summary>Bring a tab to the front.</summary>
    private void SelectTab(FitsViewerPage page)
    {
        foreach (var item in TabViewControl.TabItems)
            if (item is TabViewItem { Content: FitsViewerPage p } tab && ReferenceEquals(p, page))
            {
                TabViewControl.SelectedItem = tab;
                return;
            }
    }

    /// <summary>Close the tab at <paramref name="index"/>. False when there is no such tab.</summary>
    public bool CloseTabAt(int index)
    {
        if (index < 0 || index >= TabViewControl.TabItems.Count) return false;
        if (TabViewControl.TabItems[index] is not TabViewItem tab) return false;
        CloseTabItem(tab);
        return true;
    }

    private void CloseTabItem(TabViewItem tab)
    {
        if (tab.Tag is not FitsViewerTabItem tabItem) return;

        // Unsubscribe all handlers and clean up page resources
        if (_tabHandlers.Remove(tabItem, out var handlers))
        {
            tabItem.PropertyChanged -= handlers.HeaderHandler;
            tabItem.ViewModel.PropertyChanged -= handlers.LoadingHandler;
            if (tab.Content is FitsViewerPage page)
            {
                page.SearchAtPositionRequested -= handlers.SearchHandler;
                page.SelectingAreaChanged -= OnPageSelectingAreaChanged;
                page.CutoutRequested -= OnPageCutoutRequested;
                page.ZoomChanged -= handlers.ZoomHandler;
                page.CleanupForClose();
            }
        }

        // Stop blink if this tab is involved
        if (_blinkSession?.TabA == tabItem || _blinkSession?.TabB == tabItem)
            StopBlink();

        _tabPages.Remove(tabItem);
        ViewModel.CloseTab(tabItem);
        TabViewControl.TabItems.Remove(tab);

        if (TabViewControl.TabItems.Count == 0)
        {
            _activePage = null;
            AllTabsClosed?.Invoke();
        }
        UpdateEmptyState();
    }

    // ── Empty state + recents ────────────────────────────────────────────────

    /// <summary>
    /// The images opened before. Same store and same cap as the cube viewer's, because they are the
    /// same list of the same kind of thing in two windows.
    /// </summary>
    private readonly CanfarDesktop.Services.Fits.RecentFitsService _recents = new();

    /// <summary>Show the empty state exactly when there is nothing open, and refresh what it offers.</summary>
    private void UpdateEmptyState()
    {
        var empty = TabViewControl.TabItems.Count == 0;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (empty) RefreshRecents();
    }

    /// <summary>Rebuild the recents rows — a handful of entries, so rebuilding is cheaper than diffing.</summary>
    private void RefreshRecents()
    {
        RecentsList.Items.Clear();
        var entries = _recents.Entries;
        RecentsPanel.Visibility = entries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var entry in entries)
        {
            var button = new HyperlinkButton
            {
                Content = entry.Name,
                Tag = entry.Path,
                Padding = new Thickness(8, 2, 8, 2),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            ToolTipService.SetToolTip(button, entry.Path);
            button.Click += OnRecentClick;
            RecentsList.Items.Add(button);
        }
    }

    private async void OnOpenFileEmpty(object sender, RoutedEventArgs e) => await PromptOpenFileAsync();

    private async void OnRecentClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string path) return;

        // A file deleted since it was recorded drops out of the list rather than opening a tab that
        // can only fail.
        if (!File.Exists(path))
        {
            _recents.Remove(path);
            RefreshRecents();
            return;
        }
        await AddTabForFileAsync(path);
    }

    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TabViewControl.SelectedItem is TabViewItem { Tag: FitsViewerTabItem tabItem, Content: FitsViewerPage page })
        {
            ViewModel.ActiveTab = tabItem;
            _activePage = page;

            // Defer everything until tab is laid out
            var capturedPage = page;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (capturedPage != _activePage) return;

                // Apply shared view FIRST (crosshair + zoom), THEN sync toolbar to match
                if (ViewModel.IsLinkedCrosshairEnabled || ViewModel.IsSyncZoomEnabled)
                    ApplySharedViewToActivePage();

                SyncToolbarToActiveTab();
                UpdateWcsSyncWarning();
            });
        }
    }

    // ── Toolbar sync ─────────────────────────────────────────────────────────

    private void SyncToolbarToActiveTab()
    {
        // The pencil belongs to each image's own Marks section now, so a tab keeps whatever it was
        // left with rather than inheriting the toolbar's state — which is what a person expects of a
        // drawing mode attached to a particular picture.

        var vm = ViewModel.ActiveViewModel;
        if (vm is null) return;

        _suppressToolbarSync = true;
        try
        {
            // Select-area belongs to the page, so switching tabs shows that tab's state.
            SelectAreaToggle.IsChecked = _activePage?.SelectingArea == true;

            // Stretch combo
            var stretchName = vm.Stretch.ToString();
            for (var i = 0; i < StretchCombo.Items.Count; i++)
            {
                if (StretchCombo.Items[i] is ComboBoxItem { Tag: string tag } && tag == stretchName)
                {
                    StretchCombo.SelectedIndex = i;
                    break;
                }
            }

            // Colormap combo
            var colormapName = vm.Colormap.ToString();
            for (var i = 0; i < ColormapCombo.Items.Count; i++)
            {
                if (ColormapCombo.Items[i] is ComboBoxItem { Tag: string tag } && tag == colormapName)
                {
                    ColormapCombo.SelectedIndex = i;
                    break;
                }
            }

            // Sliders
            if (_activePage is not null)
            {
                MinCutSlider.Value = _activePage.ValueToSlider(vm.MinCut);
                MaxCutSlider.Value = _activePage.ValueToSlider(vm.MaxCut);
            }

            LoadingRing.IsActive = vm.IsLoading;
            NorthUpToggle.IsChecked = vm.IsNorthUp;

            // Zoom slider + combo
            if (_activePage is not null)
            {
                var mag = _activePage.GetZoomMagnitude();
                ZoomPresetCombo.Text = $"{mag * 100:F0}%";
            }

            // Crosshair readout in panel
            UpdatePanelCrosshairText(vm);
        }
        finally
        {
            _suppressToolbarSync = false;
        }
    }

    // ── Toolbar handlers ─────────────────────────────────────────────────────

    private async void OnOpenFile(object s, RoutedEventArgs e) => await PromptOpenFileAsync();

    private async void PromptOpenFile() => await PromptOpenFileAsync();

    private async Task PromptOpenFileAsync()
    {
        var hwnd = ActiveWindows.Count > 0
            ? WindowNative.GetWindowHandle(ActiveWindows[0])
            : nint.Zero;
        if (hwnd == nint.Zero) return;

        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add(".fits");
        picker.FileTypeFilter.Add(".fit");
        picker.FileTypeFilter.Add(".fts");
        picker.FileTypeFilter.Add("*");

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
            await AddTabForFileAsync(file.Path);
    }

    private void OnStretchChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressToolbarSync || ViewModel.ActiveViewModel is null) return;
        if (StretchCombo.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            if (Enum.TryParse<ImageStretcher.StretchMode>(tag, out var mode))
                ViewModel.ActiveViewModel.Stretch = mode;
        }
    }

    private void OnColormapChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressToolbarSync || ViewModel.ActiveViewModel is null) return;
        if (ColormapCombo.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            if (Enum.TryParse<ColormapProvider.ColormapName>(tag, out var name))
                ViewModel.ActiveViewModel.Colormap = name;
        }
    }

    private void OnMinCutChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressToolbarSync || _activePage is null || ViewModel.ActiveViewModel?.ImageData is null) return;
        ViewModel.ActiveViewModel.MinCut = _activePage.SliderToValue(e.NewValue);
    }

    private void OnMaxCutChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressToolbarSync || _activePage is null || ViewModel.ActiveViewModel?.ImageData is null) return;
        ViewModel.ActiveViewModel.MaxCut = _activePage.SliderToValue(e.NewValue);
    }

    private void OnResetStretch(object s, RoutedEventArgs e)
    {
        _activePage?.ResetView();
        SyncToolbarToActiveTab();
    }

    private void OnToggleHeader(object s, RoutedEventArgs e) => _activePage?.ToggleHeader();

    private void OnToggleNorthUp(object s, RoutedEventArgs e)
    {
        if (_activePage is null) return;
        _activePage.SetNorthUp(NorthUpToggle.IsChecked == true);
    }

    /// <summary>Arm or disarm select-area on the tab in front.</summary>
    private void OnToggleSelectArea(object s, RoutedEventArgs e)
    {
        if (_activePage is null)
        {
            SelectAreaToggle.IsChecked = false;
            return;
        }

        _activePage.SetSelectingArea(SelectAreaToggle.IsChecked == true);
    }

    /// <summary>
    /// Follow the page when the mode turns ITSELF off, which it does after one region.
    ///
    /// The button is a picture of the page's state, not a second copy of it — a toggle left lit over
    /// a mode that has already ended is worse than no button, because the next drag pans and nobody
    /// can see why.
    /// </summary>
    private void OnPageSelectingAreaChanged()
        => SelectAreaToggle.IsChecked = _activePage?.SelectingArea == true;

    // ── Zoom slider ─────────────────────────────────────────────────────────

    private void OnZoomPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressToolbarSync || _activePage is null) return;

        double pct = 0;
        if (ZoomPresetCombo.SelectedItem is ComboBoxItem { Tag: string tag })
            NumberInput.TryParseWire(tag, out pct); // the presets' own tags: invariant
        else if (!string.IsNullOrEmpty(ZoomPresetCombo.Text))
        {
            // Handle manual text entry: "300" or "300%"
            var text = ZoomPresetCombo.Text.Trim().TrimEnd('%');
            NumberInput.TryParseUser(text, out pct); // as typed: a point or a comma, whatever the culture
        }

        if (pct > 0)
        {
            _activePage.SetZoomLevel(pct / 100.0);
            _suppressToolbarSync = true;
            ZoomPresetCombo.Text = $"{pct:F0}%";
            _suppressToolbarSync = false;
            if (ViewModel.IsSyncZoomEnabled) UpdateSharedAngularZoom();
        }
    }

    private void OnZoomTextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
    {
        if (_suppressToolbarSync || _activePage is null) return;
        var text = args.Text?.Trim().TrimEnd('%') ?? "";
        if (NumberInput.TryParseUser(text, out var pct) && pct > 0)
        {
            args.Handled = true;
            _activePage.SetZoomLevel(pct / 100.0);
            _suppressToolbarSync = true;
            ZoomPresetCombo.Text = $"{pct:F0}%";
            _suppressToolbarSync = false;
            if (ViewModel.IsSyncZoomEnabled) UpdateSharedAngularZoom();
        }
    }

    /// <summary>Called by the page when zoom changes via scroll wheel.</summary>
    public void UpdateZoomDisplay(double zoomMagnitude)
    {
        _suppressToolbarSync = true;
        ZoomPresetCombo.Text = $"{zoomMagnitude * 100:F0}%";
        _suppressToolbarSync = false;
    }

    // ── Crosshair toolbar ────────────────────────────────────────────────────

    private void OnCopyCoords(object s, RoutedEventArgs e) => _activePage?.CopyCoords();

    private void OnSearchAtPosition(object s, RoutedEventArgs e) => _activePage?.TriggerSearchAtPosition();

    private void OnClearCrosshair(object s, RoutedEventArgs e) => _activePage?.ClearCrosshair();

    private void OnSaveCrosshair(object s, RoutedEventArgs e) => SaveCurrentCrosshair("");

    // ── Sync zoom ─────────────────────────────────────────────────────────

    private void OnToggleSyncZoom(object s, RoutedEventArgs e)
    {
        ViewModel.IsSyncZoomEnabled = SyncZoomToggle.IsChecked == true;
        if (ViewModel.IsSyncZoomEnabled)
            UpdateSharedAngularZoom();
        UpdateWcsSyncWarning();
    }

    /// <summary>
    /// Show the "approximate WCS" banner when a sync mode is active but at least one open image
    /// lacks a precise WCS solution (missing/invalid, or reconstructed from legacy pointing keywords
    /// like a raw DAO frame). Sync maps positions through each image's WCS, so an approximate one
    /// makes the linked crosshair and matched zoom unreliable — the user should know why.
    /// </summary>
    private void UpdateWcsSyncWarning()
    {
        var syncing = ViewModel.IsSyncZoomEnabled || ViewModel.IsLinkedCrosshairEnabled;
        var anyImprecise = false;
        if (syncing)
        {
            foreach (var tab in ViewModel.Tabs)
            {
                var wcs = tab.ViewModel.ImageData?.Wcs;
                if (wcs is null || !wcs.IsValid || wcs.IsApproximate) { anyImprecise = true; break; }
            }
        }
        WcsSyncWarningBar.IsOpen = syncing && anyImprecise;
    }

    /// <summary>
    /// Store the current active tab's zoom as angular units (arcsec/screen-pixel).
    /// Other tabs will interpret this when they become visible.
    /// </summary>
    private void UpdateSharedAngularZoom()
    {
        if (_activePage is null || ViewModel.ActiveTab is null) return;
        var wcs = ViewModel.ActiveTab.ViewModel.ImageData?.Wcs;
        if (wcs is null || !wcs.IsValid) return;
        var mag = _activePage.GetZoomMagnitude();
        if (mag > 0)
            ViewModel.SharedAngularZoom = wcs.PixelScaleArcsec / mag;
    }

    /// <summary>
    /// Apply shared view state (angular zoom + crosshair center) to the active page.
    /// Called when switching tabs or when shared state changes.
    /// </summary>
    /// <summary>
    /// Apply shared view state to the active page. Called ONLY when a tab becomes visible.
    /// Reads from the single source of truth (HostVM) and applies locally.
    /// Never touches hidden tabs. Never propagates.
    /// </summary>
    private void ApplySharedViewToActivePage()
    {
        if (_activePage is null || ViewModel.ActiveTab is null) return;

        var vm = ViewModel.ActiveTab.ViewModel;
        var wcs = vm.ImageData?.Wcs;
        if (wcs is not { IsValid: true }) return;

        // Step 1: Convert shared RA/Dec to this tab's image pixel and set crosshair
        if (ViewModel.IsLinkedCrosshairEnabled && ViewModel.LinkedCrosshairPosition is { } pos)
        {
            var pixel = wcs.WorldToPixel(pos.Ra, pos.Dec);
            if (pixel is not null)
            {
                var displayX = pixel.Value.Px - 1;
                var displayY = vm.ImageData!.Height - 1 - (pixel.Value.Py - 1);
                _activePage.SetCrosshairAtImagePixel(displayX, displayY);
            }
        }

        // Step 2: Apply matched zoom (this will center on crosshair if one was set in step 1)
        if (ViewModel.IsSyncZoomEnabled && ViewModel.SharedAngularZoom is > 0 and var angZoom)
        {
            var localZoom = wcs.PixelScaleArcsec / angZoom;
            _activePage.SetZoomLevel(localZoom);
        }

        // Step 3: Update panel display
        UpdatePanelCrosshairText(vm);
    }

    // ── Keyboard ─────────────────────────────────────────────────────────────

    private void OnKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (_blinkSession is null) return;

        switch (e.Key)
        {
            case Windows.System.VirtualKey.Escape:
                StopBlink();
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Space:
                _blinkSession.IsPaused = !_blinkSession.IsPaused;
                if (ViewModel.ActiveViewModel is not null)
                    ViewModel.ActiveViewModel.StatusMessage = _blinkSession.IsPaused ? Loc.T("Fits_BlinkPaused") : Loc.T("Fits_Blinking");
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Left:
                _blinkOpacity = 0;
                _activePage?.SetBlinkOpacity(0); // show image A
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Right:
                _blinkOpacity = 1;
                _activePage?.SetBlinkOpacity(1); // show image B
                e.Handled = true;
                break;
        }
    }

    // ── Blink comparison ────────────────────────────────────────────────────

    private bool StartBlink(FitsViewerTabItem tabA, FitsViewerTabItem tabB)
    {
        if (_activePage is null) return false;
        StopBlink();

        var wcsA = tabA.ViewModel.ImageData?.Wcs;
        var wcsB = tabB.ViewModel.ImageData?.Wcs;
        if (wcsA is null || !wcsA.IsValid || wcsB is null || !wcsB.IsValid)
        {
            if (ViewModel.ActiveViewModel is not null)
                ViewModel.ActiveViewModel.StatusMessage = Loc.T("Fits_BlinkNeedsWcs");
            return false;
        }

        var bitmapB = tabB.ViewModel.RenderedImage;
        if (bitmapB is null) return false;

        // Compute overlay transform for image B to match A's current view
        var pos = tabA.ViewModel.CrosshairPosition;
        var refRa = pos?.Ra ?? wcsA.CrVal1;
        var refDec = pos?.Dec ?? wcsA.CrVal2;

        var ct = _activePage.GetCurrentTransform();
        var imgDataA = tabA.ViewModel.ImageData!;
        var imgDataB = tabB.ViewModel.ImageData!;

        // Use CANVAS dimensions (not page — page includes header panel)
        var (canvasW, canvasH) = _activePage.GetCanvasSize();
        // Use FitsImage's display size (BlinkImage will be forced to match via Stretch=Fill)
        var (fitsDisplayW, fitsDisplayH) = _activePage.GetImageDisplaySize();

        // Frame the SHARED (smaller) field so BOTH images are comparable. The two frames usually
        // cover very different angular fields (a wide OMM frame vs a narrow HST cutout); whichever
        // is wider is zoomed IN to the overlap and centred on the reference, so the narrower one
        // fills the view instead of rendering as a tiny square. A's view is restored on Stop.
        var fieldA = imgDataA.Width * wcsA.PixelScaleArcsec;
        var fieldB = imgDataB.Width * wcsB.PixelScaleArcsec;
        var minField = Math.Min(fieldA, fieldB);
        if (minField <= 0) return false;

        _blinkRestore = (_activePage, ct);
        var signA = ct.scaleX < 0 ? -1.0 : 1.0;
        var zoomA = fieldA / minField; // A frames the overlap (1.0 when A is already the narrower)
        var pixelA = wcsA.WorldToPixel(refRa, refDec);
        if (pixelA is null) return false;
        var displayXA = (pixelA.Value.Px - 1) / imgDataA.Width * fitsDisplayW;
        var displayYA = (imgDataA.Height - 1 - (pixelA.Value.Py - 1)) / imgDataA.Height * fitsDisplayH;
        var (txA, tyA) = ViewportMath.ComputeCenterTranslate(
            displayXA, displayYA, signA * zoomA, zoomA, ct.rotation,
            fitsDisplayW, fitsDisplayH, canvasW, canvasH);
        _activePage.SetRawTransform(ct.rotation, signA * zoomA, zoomA, txA, tyA);

        // B's matched zoom = zoomA · fieldB/fieldA = fieldB/minField (also frames the overlap).
        var matchedZoom = ViewportMath.ComputeMatchedZoom(zoomA, fieldA, fieldB);

        // Match A's sky orientation: rotB = rotA + NorthAngleA - NorthAngleB
        var rotationB = ct.rotation + wcsA.NorthAngle - wcsB.NorthAngle;
        var flipB = wcsB.HasParityFlip != (signA < 0);
        var scaleXB = flipB ? -matchedZoom : matchedZoom;

        // Map B's reference pixel to FitsImage's display space (Stretch=Fill)
        var pixelB = wcsB.WorldToPixel(refRa, refDec);
        if (pixelB is null) return false;
        var displayX = (pixelB.Value.Px - 1) / imgDataB.Width * fitsDisplayW;
        var displayY = (imgDataB.Height - 1 - (pixelB.Value.Py - 1)) / imgDataB.Height * fitsDisplayH;

        // Compute translate to center reference point using FitsImage's coordinate space
        var (txB, tyB) = ViewportMath.ComputeCenterTranslate(
            displayX, displayY,
            scaleXB, matchedZoom, rotationB,
            fitsDisplayW, fitsDisplayH,
            canvasW, canvasH);

        var transformB = new BlinkAligner.BlinkTransform(rotationB, scaleXB, matchedZoom, txB, tyB);

        _blinkSession = new BlinkSession
        {
            TabA = tabA, TabB = tabB,
            ReferenceRa = refRa, ReferenceDec = refDec,
            IntervalMs = (int)BlinkIntervalSlider.Value
        };

        // Enter blink: overlay image B on top of A, start fade cycle
        _activePage.EnterBlinkMode(bitmapB, transformB);

        _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) }; // smooth fade step
        _blinkTimer.Tick += OnBlinkTick;
        _blinkTimer.Start();

        BlinkIntervalSlider.Visibility = Visibility.Visible;
        StopBlinkButton.Visibility = Visibility.Visible;
        if (ViewModel.ActiveViewModel is not null)
            ViewModel.ActiveViewModel.StatusMessage = Loc.F("Fits_BlinkingStatus", tabA.Header, tabB.Header);
        return true;
    }

    private void StopBlink()
    {
        if (_blinkTimer is not null)
        {
            _blinkTimer.Tick -= OnBlinkTick;
            _blinkTimer.Stop();
        }
        _blinkTimer = null;
        _blinkSession = null;
        _activePage?.ExitBlinkMode();
        // Restore image A's pre-blink view (blink zoomed/centred it to frame the overlap).
        if (_blinkRestore is { } r)
        {
            r.page.SetRawTransform(r.transform.rotation, r.transform.scaleX, r.transform.scaleY,
                r.transform.translateX, r.transform.translateY);
            _blinkRestore = null;
        }
        BlinkIntervalSlider.Visibility = Visibility.Collapsed;
        StopBlinkButton.Visibility = Visibility.Collapsed;
    }

    private double _blinkOpacity;
    private bool _blinkFadingIn = true;

    private void OnBlinkTick(object? sender, object e)
    {
        if (_blinkSession is null || _blinkSession.IsPaused) return;

        // Smooth fade: step opacity toward target
        var speed = 50.0 / Math.Max(_blinkSession.IntervalMs, 100); // fraction per tick
        if (_blinkFadingIn)
        {
            _blinkOpacity = Math.Min(1.0, _blinkOpacity + speed);
            if (_blinkOpacity >= 1.0) _blinkFadingIn = false;
        }
        else
        {
            _blinkOpacity = Math.Max(0.0, _blinkOpacity - speed);
            if (_blinkOpacity <= 0.0) _blinkFadingIn = true;
        }

        _activePage?.SetBlinkOpacity(_blinkOpacity);
    }

    private void OnBlinkIntervalChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_blinkSession is not null)
            _blinkSession.IntervalMs = (int)e.NewValue;
    }

    private void OnStopBlink(object s, RoutedEventArgs e) => StopBlink();

    private void OnStartBlinkWithTab(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: FitsViewerTabItem otherTab }) return;
        if (ViewModel.ActiveTab is null || ViewModel.ActiveTab == otherTab) return;
        StartBlink(ViewModel.ActiveTab, otherTab);
    }

    private void OnBlinkFlyoutOpening(object sender, object e)
    {
        if (sender is not MenuFlyout flyout) return;
        foreach (var item in flyout.Items.OfType<MenuFlyoutItem>())
            item.Click -= OnStartBlinkWithTab;
        flyout.Items.Clear();
        foreach (var tab in ViewModel.Tabs)
        {
            if (tab == ViewModel.ActiveTab) continue;
            flyout.Items.Add(new MenuFlyoutItem
            {
                Text = Loc.F("Fits_BlinkWith", tab.Header),
                Tag = tab,
            });
            ((MenuFlyoutItem)flyout.Items[^1]).Click += OnStartBlinkWithTab;
        }
        if (flyout.Items.Count == 0)
            flyout.Items.Add(new MenuFlyoutItem { Text = Loc.T("Fits_OpenAnotherTab"), IsEnabled = false });
    }

    // ── Linked crosshair ───────────────────────────────────────────────────

    private void OnToggleLinkedCrosshair(object s, RoutedEventArgs e)
    {
        ViewModel.IsLinkedCrosshairEnabled = LinkedCrosshairToggle.IsChecked == true;
        if (ViewModel.IsLinkedCrosshairEnabled)
        {
            // Auto-enable North Up on active tab for consistency
            if (_activePage is not null && ViewModel.ActiveTab is not null
                && !ViewModel.ActiveTab.ViewModel.IsNorthUp)
            {
                _activePage.SetNorthUp(true);
                NorthUpToggle.IsChecked = true;
            }
            // Write current crosshair to shared state
            if (ViewModel.ActiveTab?.ViewModel.CrosshairPosition is { } pos)
                ViewModel.LinkedCrosshairPosition = pos;
        }
        else
        {
            ViewModel.LinkedCrosshairPosition = null;
            _activePage?.ClearLinkedCrosshair();
        }
        UpdateWcsSyncWarning();
    }

    // ── Coordinate panel ─────────────────────────────────────────────────────

    private void UpdatePanelCrosshairText(FitsViewerViewModel? vm)
    {
        if (vm?.CrosshairPosition is { } pos)
        {
            PanelCrosshairText.Text = Loc.F("Fits_CrosshairReadout", pos.FormattedRa, pos.FormattedDec);
            PanelCrosshairText.Foreground = (Microsoft.UI.Xaml.Media.Brush)
                Application.Current.Resources["TextFillColorPrimaryBrush"];
        }
        else
        {
            PanelCrosshairText.Text = Loc.T("Fits_NoCrosshair");
            PanelCrosshairText.Foreground = (Microsoft.UI.Xaml.Media.Brush)
                Application.Current.Resources["TextFillColorTertiaryBrush"];
        }
    }

    private void OnToggleCoordPanel(object s, RoutedEventArgs e)
    {
        _coordPanelVisible = !_coordPanelVisible;
        CoordPanelColumn.Width = _coordPanelVisible ? new GridLength(280) : new GridLength(0);
    }

    private void OnSaveCrosshairFromPanel(object s, RoutedEventArgs e)
    {
        SaveCurrentCrosshair(CoordLabelBox.Text.Trim());
        CoordLabelBox.Text = "";
    }

    private void SaveCurrentCrosshair(string label)
    {
        var vm = ViewModel.ActiveViewModel;
        if (vm?.CrosshairPosition is null)
        {
            if (vm is not null) vm.StatusMessage = Loc.T("Fits_PlaceCrosshairFirst");
            return;
        }
        ViewModel.SaveCoordinate(label, vm.CrosshairPosition.Ra, vm.CrosshairPosition.Dec, vm.FilePath);
    }

    private void OnGoToManual(object s, RoutedEventArgs e)
    {
        if (_activePage is null) return;
        // As a person types a position anywhere else in the app: degrees with a point or a comma, or
        // sexagesimal. double.TryParse read it by the machine's culture, and on a French Windows "10.68"
        // was not a number.
        if (!Sexagesimal.TryParseAngle(ManualRaBox.Text, isRa: true, out var ra) ||
            !Sexagesimal.TryParseAngle(ManualDecBox.Text, isRa: false, out var dec))
        {
            if (ViewModel.ActiveViewModel is not null)
                ViewModel.ActiveViewModel.StatusMessage = Loc.T("Fits_EnterValidCoords");
            return;
        }
        _activePage.GoToWorldCoordinate(ra, dec);
    }

    private void OnGoToSaved(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SavedCoordinate coord } && _activePage is not null)
            _activePage.GoToWorldCoordinate(coord.Ra, coord.Dec);
    }

    private void OnDeleteCoord(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SavedCoordinate coord })
            ViewModel.DeleteCoordinate(coord);
    }

    // ── MCP surface (steer + read + probe + goto the active FITS tab) ─────────────
    // Mirrors the cube viewer's MCP surface. The host is the single entry point: it owns the active
    // page + active view model + the toolbar sync, so an agent drives the exact same path the user does.

    /// <summary>A snapshot of the active FITS tab's file + display state for get_fits_view.</summary>
    public FitsViewState GetFitsViewState()
    {
        var vm = ViewModel.ActiveViewModel;
        var img = vm?.ImageData;
        var cross = vm?.CrosshairPosition;
        var wcs = img?.Wcs;
        bool wcsValid = wcs is { IsValid: true };
        var crossPx = _activePage?.CrosshairDisplayPixel;
        double zoomPct = _activePage is not null ? _activePage.GetZoomMagnitude() * 100.0 : 100.0;
        return new FitsViewState(
            // Ready, not merely parsed: the pixels exist a moment before the first render finishes,
            // and "loaded" and "loading" both true at once left a poller not knowing which to believe.
            Loaded: vm is not null && img is not null && !vm.IsLoading,
            FileName: vm?.FilePath is { } fp ? System.IO.Path.GetFileName(fp) : (vm?.Title ?? ""),
            Width: img?.Width ?? 0,
            Height: img?.Height ?? 0,
            Stretch: (vm?.Stretch ?? ImageStretcher.StretchMode.Linear).ToString(),
            Colormap: (vm?.Colormap ?? ColormapProvider.ColormapName.Grayscale).ToString(),
            MinCut: vm?.MinCut ?? 0,
            MaxCut: vm?.MaxCut ?? 0,
            ZoomPercent: Math.Round(zoomPct, 1),
            NorthUp: vm?.IsNorthUp ?? false,
            SelectingArea: _activePage?.SelectingArea ?? false,
            HasWcs: wcsValid,
            CrosshairPlaced: cross is not null,
            CrosshairRa: cross?.Ra ?? 0,
            CrosshairDec: cross?.Dec ?? 0,
            // Full read parity: the image-info panel, HDUs, toggles, blink, panels, crosshair pixel.
            Path: vm?.FilePath ?? "",
            HduIndex: vm?.SelectedHduIndex ?? 0,
            Hdus: _activePage?.HduInfos().Select(h => new FitsHduInfo(h.Index, h.Name, h.Shape, h.IsImage)).ToList(),
            Unit: img?.Unit ?? "",
            DataMin: img?.Min ?? 0,
            DataMax: img?.Max ?? 0,
            PixelScaleArcsec: wcsValid ? wcs!.PixelScaleArcsec : 0,
            NorthAngleDeg: wcsValid ? wcs!.NorthAngle : 0,
            ParityFlip: wcsValid && wcs!.HasParityFlip,
            WcsApproximate: wcsValid && wcs!.IsApproximate,
            SyncZoom: ViewModel.IsSyncZoomEnabled,
            LinkedCrosshair: ViewModel.IsLinkedCrosshairEnabled,
            BlinkActive: _blinkSession is not null,
            HeaderPanelOpen: _activePage?.HeaderPanelVisible ?? false,
            BookmarksPanelOpen: _coordPanelVisible,
            CrosshairX: crossPx?.X,
            CrosshairY: crossPx?.Y,
            Status: vm?.StatusMessage ?? "",
            Loading: vm?.IsLoading ?? false);
    }

    /// <summary>Apply display settings from MCP to the active tab; each null is left unchanged.</summary>
    public FitsViewState ApplyFitsView(
        string? stretch = null, string? colormap = null, double? minCut = null, double? maxCut = null,
        double? zoomPercent = null, bool? northUp = null, bool? reset = null, bool? clearCrosshair = null,
        int? hdu = null, int? crosshairX = null, int? crosshairY = null,
        int? centerX = null, int? centerY = null,
        bool? syncZoom = null, bool? linkedCrosshair = null,
        bool? showHeaderPanel = null, bool? showBookmarksPanel = null,
        bool? showMarksPanel = null,
        bool? selectArea = null)
    {
        if (selectArea is not null && _activePage is not null)
        {
            _activePage.SetSelectingArea(selectArea.Value);
            SelectAreaToggle.IsChecked = _activePage.SelectingArea;
        }

        // HDU switch FIRST — it resets the cuts to the new extension's auto-cut, so explicit
        // cut/stretch values passed in the same call win over the reset.
        if (hdu is not null && _activePage?.SelectHduByIndex(hdu.Value) == false
            && ViewModel.ActiveViewModel is { } hvm)
            hvm.StatusMessage = $"HDU {hdu.Value} is not a viewable image extension";

        var vm = ViewModel.ActiveViewModel;
        if (vm is not null)
        {
            // Setting these view-model properties re-renders automatically (OnXChanged → RenderAsync).
            if (!string.IsNullOrEmpty(stretch) && Enum.TryParse<ImageStretcher.StretchMode>(stretch, true, out var sm))
                vm.Stretch = sm;
            if (!string.IsNullOrEmpty(colormap) && Enum.TryParse<ColormapProvider.ColormapName>(colormap, true, out var cm))
                vm.Colormap = cm;
            if (minCut is not null) vm.MinCut = (float)minCut.Value;
            if (maxCut is not null) vm.MaxCut = (float)maxCut.Value;
        }
        if (_activePage is not null)
        {
            if (reset == true) _activePage.ResetView();                          // before zoom, so explicit zoom wins
            if (zoomPercent is not null) _activePage.SetZoomLevel(zoomPercent.Value / 100.0);
            if (northUp is not null) _activePage.SetNorthUp(northUp.Value);
            if (clearCrosshair == true) _activePage.ClearCrosshair();
            if (crosshairX is not null && crosshairY is not null)
                _activePage.PlaceCrosshairAtPixel(crosshairX.Value, crosshairY.Value);
            if (centerX is not null && centerY is not null)
                _activePage.CenterViewportOnPixel(centerX.Value, centerY.Value);
            if (showHeaderPanel is not null) _activePage.SetHeaderPanelVisible(showHeaderPanel.Value);
        }
        // The cross-tab toggles run their toolbar handlers so side effects (auto-North-Up, shared
        // state seeding, WCS warning) match the user clicking the buttons.
        if (syncZoom is not null)
        {
            SyncZoomToggle.IsChecked = syncZoom.Value;
            OnToggleSyncZoom(this, new RoutedEventArgs());
        }
        if (linkedCrosshair is not null)
        {
            LinkedCrosshairToggle.IsChecked = linkedCrosshair.Value;
            OnToggleLinkedCrosshair(this, new RoutedEventArgs());
        }
        if (showBookmarksPanel is not null)
        {
            _coordPanelVisible = showBookmarksPanel.Value;
            CoordPanelColumn.Width = _coordPanelVisible ? new GridLength(280) : new GridLength(0);
        }
        // Through the page's own opener, which also brings the column it lives in into view — the
        // marks list is inside the header column, so setting the expander alone would open a section
        // nobody can see.
        if (showMarksPanel == true) _activePage?.ShowMarksPanel();
        else if (showMarksPanel == false) _activePage?.HideMarksPanel();
        SyncToolbarToActiveTab(); // keep the toolbar in sync with the programmatic change
        return GetFitsViewState();
    }

    /// <summary>
    /// Every open FITS tab's index, name, real path and active flag (the MCP list_open_tabs detail).
    ///
    /// The path is what makes the listing actionable: a name alone tells an agent that something is
    /// open and gives it no way to reopen it later.
    /// </summary>
    public IReadOnlyList<CanfarDesktop.Mcp.Tools.Write.ViewerTabInfo> TabInfos()
    {
        var infos = new List<CanfarDesktop.Mcp.Tools.Write.ViewerTabInfo>(TabViewControl.TabItems.Count);
        for (int i = 0; i < TabViewControl.TabItems.Count; i++)
        {
            var tab = TabViewControl.TabItems[i] as TabViewItem;
            var item = tab?.Tag as FitsViewerTabItem;
            infos.Add(new CanfarDesktop.Mcp.Tools.Write.ViewerTabInfo(
                i,
                item?.Header ?? tab?.Header as string ?? "",
                item?.ViewModel.FilePath,
                ReferenceEquals(tab, TabViewControl.SelectedItem)));
        }
        return infos;
    }

    /// <summary>Make the tab at a 0-based index active (the MCP switch_fits_tab tool).</summary>
    public bool SwitchToTab(int index)
    {
        if (index < 0 || index >= TabViewControl.TabItems.Count) return false;
        TabViewControl.SelectedItem = TabViewControl.TabItems[index];
        return true;
    }

    /// <summary>
    /// MCP blink control (blink_fits_tabs): start against a partner tab (indices match TabInfos),
    /// stop, pause/resume; intervalMs updates the fade speed with any action. Same code path as
    /// the toolbar (StartBlink's WCS checks + shared-field framing + view restore on stop).
    /// </summary>
    public CanfarDesktop.Mcp.Tools.Write.FitsBlinkOutcome ControlBlink(string action, int? withTabIndex, int? intervalMs)
    {
        if (intervalMs is { } iv)
            BlinkIntervalSlider.Value = Math.Clamp(iv, 500, 5000); // OnBlinkIntervalChanged updates a live session

        switch (action)
        {
            case "start":
            {
                if (ViewModel.ActiveTab is null || _activePage is null)
                    return new(false, false, null, CurrentInterval(), "no FITS tab is open");
                if (withTabIndex is not { } idx || idx < 0 || idx >= TabViewControl.TabItems.Count)
                    return new(false, false, null, CurrentInterval(),
                        $"withTabIndex must be 0..{TabViewControl.TabItems.Count - 1} (see list_open_tabs fitsTabs)");
                if ((TabViewControl.TabItems[idx] as TabViewItem)?.Tag is not FitsViewerTabItem other)
                    return new(false, false, null, CurrentInterval(), "tab not found");
                if (other == ViewModel.ActiveTab)
                    return new(false, false, null, CurrentInterval(), "withTabIndex is the ACTIVE tab — pass the partner tab's index");
                bool ok = StartBlink(ViewModel.ActiveTab, other);
                return new(ok, false, ok ? other.Header : null, CurrentInterval(),
                    ok ? null : ViewModel.ActiveViewModel?.StatusMessage ?? "blink could not start (both images need a valid WCS)");
            }
            case "stop":
                StopBlink();
                return new(false, false, null, CurrentInterval(), null);
            case "pause":
            case "resume":
                if (_blinkSession is null)
                    return new(false, false, null, CurrentInterval(), "no blink comparison is running — start one first");
                _blinkSession.IsPaused = action == "pause";
                return new(true, _blinkSession.IsPaused, _blinkSession.TabB.Header, _blinkSession.IntervalMs, null);
            default:
                return new(_blinkSession is not null, _blinkSession?.IsPaused ?? false,
                    _blinkSession?.TabB.Header, CurrentInterval(), "unknown action");
        }

        int CurrentInterval() => _blinkSession?.IntervalMs ?? (int)BlinkIntervalSlider.Value;
    }

    /// <summary>Pixel value + sky coordinate at a 0-based display pixel, or null if out of range.</summary>
    public FitsPixelResult? ProbeFitsPixel(int x, int y)
    {
        var img = ViewModel.ActiveViewModel?.ImageData;
        if (img is null || x < 0 || y < 0 || x >= img.Width || y >= img.Height) return null;

        var (_, arrayY) = PixelConvention.DisplayToArray(x, y, img.Height);
        double raw = img.Pixels[(int)arrayY * img.Width + x];
        double? value = double.IsFinite(raw) ? raw : null; // blanked pixel → null (NaN is not valid JSON)
        if (img.Wcs is { IsValid: true } wcs)
        {
            var (ra, dec) = PixelConvention.SkyAtDisplay(wcs, img.Height, x, y);
            return new FitsPixelResult(x, y, value, true, ra, dec, img.Unit);
        }
        return new FitsPixelResult(x, y, value, false, 0, 0, img.Unit);
    }

    /// <summary>Center the active viewport on an RA/Dec (degrees) and place the crosshair there.</summary>
    public FitsGotoOutcome GotoFitsCoordinate(double ra, double dec)
    {
        var vm = ViewModel.ActiveViewModel;
        if (vm?.ImageData is null) return new FitsGotoOutcome(false, ra, dec, "no FITS image is loaded");
        if (vm.ImageData.Wcs is not { IsValid: true }) return new FitsGotoOutcome(false, ra, dec, "the loaded FITS has no valid WCS");
        if (_activePage is null) return new FitsGotoOutcome(false, ra, dec, "no active FITS tab");

        var target = _activePage.GoToWorldCoordinate(ra, dec); // centres the viewport + places the crosshair
        return new FitsGotoOutcome(target.OnImage, ra, dec, target.Why);
    }
}
