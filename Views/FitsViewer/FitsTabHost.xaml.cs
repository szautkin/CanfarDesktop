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
    public event Action? AllTabsClosed;

    private FitsViewerPage? _activePage;
    private bool _suppressToolbarSync;
    private bool _coordPanelVisible;
    private BlinkSession? _blinkSession;
    private DispatcherTimer? _blinkTimer;

    public FitsTabHost(FitsTabHostViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        CoordListView.ItemsSource = ViewModel.SavedCoordinates;

        // Pre-select the 100% item (editable ComboBox ignores Text= on first render)
        Loaded += (_, _) =>
        {
            _suppressToolbarSync = true;
            ZoomPresetCombo.SelectedIndex = 3; // "100%" item
            _suppressToolbarSync = false;
        };
    }

    // ── Tab lifecycle ────────────────────────────────────────────────────────

    public FitsViewerPage AddNewTab()
    {
        var tabItem = ViewModel.AddNewTab();
        return CreateTabViewItem(tabItem);
    }

    public async Task<FitsViewerPage> AddTabForFileAsync(string filePath)
    {
        var tabItem = ViewModel.AddNewTab();
        var page = CreateTabViewItem(tabItem);
        await page.OpenFileAsync(filePath);
        SyncToolbarToActiveTab();
        return page;
    }

    private record TabHandlers(
        System.ComponentModel.PropertyChangedEventHandler HeaderHandler,
        System.ComponentModel.PropertyChangedEventHandler LoadingHandler,
        Action<double, double> SearchHandler,
        Action<double> ZoomHandler);

    private readonly Dictionary<FitsViewerTabItem, TabHandlers> _tabHandlers = [];
    private readonly Dictionary<FitsViewerTabItem, FitsViewerPage> _tabPages = [];

    private FitsViewerPage CreateTabViewItem(FitsViewerTabItem tabItem)
    {
        var page = new FitsViewerPage(tabItem.ViewModel);

        // Store handlers so we can unsubscribe on tab close
        Action<double, double> searchHandler = (ra, dec) => SearchAtPositionRequested?.Invoke(ra, dec);
        page.SearchAtPositionRequested += searchHandler;

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

    private void OnTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Tab.Tag is not FitsViewerTabItem tabItem) return;

        // Unsubscribe all handlers and clean up page resources
        if (_tabHandlers.Remove(tabItem, out var handlers))
        {
            tabItem.PropertyChanged -= handlers.HeaderHandler;
            tabItem.ViewModel.PropertyChanged -= handlers.LoadingHandler;
            if (args.Tab.Content is FitsViewerPage page)
            {
                page.SearchAtPositionRequested -= handlers.SearchHandler;
                page.ZoomChanged -= handlers.ZoomHandler;
                page.CleanupForClose();
            }
        }

        // Stop blink if this tab is involved
        if (_blinkSession?.TabA == tabItem || _blinkSession?.TabB == tabItem)
            StopBlink();

        _tabPages.Remove(tabItem);
        ViewModel.CloseTab(tabItem);
        sender.TabItems.Remove(args.Tab);

        if (sender.TabItems.Count == 0)
        {
            _activePage = null;
            AllTabsClosed?.Invoke();
        }
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
            });
        }
    }

    // ── Toolbar sync ─────────────────────────────────────────────────────────

    private void SyncToolbarToActiveTab()
    {
        var vm = ViewModel.ActiveViewModel;
        if (vm is null) return;

        _suppressToolbarSync = true;
        try
        {
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

    // ── Zoom slider ─────────────────────────────────────────────────────────

    private void OnZoomPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressToolbarSync || _activePage is null) return;

        double pct = 0;
        if (ZoomPresetCombo.SelectedItem is ComboBoxItem { Tag: string tag })
            double.TryParse(tag, out pct);
        else if (!string.IsNullOrEmpty(ZoomPresetCombo.Text))
        {
            // Handle manual text entry: "300" or "300%"
            var text = ZoomPresetCombo.Text.Trim().TrimEnd('%');
            double.TryParse(text, out pct);
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
        if (double.TryParse(text, out var pct) && pct > 0)
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
                    ViewModel.ActiveViewModel.StatusMessage = _blinkSession.IsPaused ? "Blink paused (Space to resume)" : "Blinking...";
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

    private void StartBlink(FitsViewerTabItem tabA, FitsViewerTabItem tabB)
    {
        if (_activePage is null) return;
        StopBlink();

        var wcsA = tabA.ViewModel.ImageData?.Wcs;
        var wcsB = tabB.ViewModel.ImageData?.Wcs;
        if (wcsA is null || !wcsA.IsValid || wcsB is null || !wcsB.IsValid)
        {
            if (ViewModel.ActiveViewModel is not null)
                ViewModel.ActiveViewModel.StatusMessage = "Both images need valid WCS for blink";
            return;
        }

        var bitmapB = tabB.ViewModel.RenderedImage;
        if (bitmapB is null) return;

        // Compute overlay transform for image B to match A's current view
        var pos = tabA.ViewModel.CrosshairPosition;
        var refRa = pos?.Ra ?? wcsA.CrVal1;
        var refDec = pos?.Dec ?? wcsA.CrVal2;

        var ct = _activePage.GetCurrentTransform();
        var zoomA = Math.Abs(ct.scaleX);
        var matchedZoom = ViewportMath.ComputeMatchedZoom(zoomA, wcsA.PixelScaleArcsec, wcsB.PixelScaleArcsec);

        // Match A's sky orientation: rotB = rotA + NorthAngleA - NorthAngleB
        var rotationB = ct.rotation + wcsA.NorthAngle - wcsB.NorthAngle;
        var flipB = wcsB.HasParityFlip != (ct.scaleX < 0);
        var scaleXB = flipB ? -matchedZoom : matchedZoom;

        // Use CANVAS dimensions (not page — page includes header panel)
        var (canvasW, canvasH) = _activePage.GetCanvasSize();
        // Use FitsImage's display size (BlinkImage will be forced to match via Stretch=Fill)
        var (fitsDisplayW, fitsDisplayH) = _activePage.GetImageDisplaySize();
        var imgDataB = tabB.ViewModel.ImageData!;

        // Map B's reference pixel to FitsImage's display space (Stretch=Fill)
        var pixelB = wcsB.WorldToPixel(refRa, refDec);
        if (pixelB is null) return;
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
            ViewModel.ActiveViewModel.StatusMessage = $"Blinking: {tabA.Header} ↔ {tabB.Header} (Esc to stop)";
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
                Text = $"Blink with {tab.Header}",
                Tag = tab,
            });
            ((MenuFlyoutItem)flyout.Items[^1]).Click += OnStartBlinkWithTab;
        }
        if (flyout.Items.Count == 0)
            flyout.Items.Add(new MenuFlyoutItem { Text = "Open another tab first", IsEnabled = false });
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
    }

    // ── Coordinate panel ─────────────────────────────────────────────────────

    private void UpdatePanelCrosshairText(FitsViewerViewModel? vm)
    {
        if (vm?.CrosshairPosition is { } pos)
        {
            PanelCrosshairText.Text = $"RA  {pos.FormattedRa}\nDec {pos.FormattedDec}";
            PanelCrosshairText.Foreground = (Microsoft.UI.Xaml.Media.Brush)
                Application.Current.Resources["TextFillColorPrimaryBrush"];
        }
        else
        {
            PanelCrosshairText.Text = "No crosshair placed";
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
            if (vm is not null) vm.StatusMessage = "Right-click to place crosshair first";
            return;
        }
        ViewModel.SaveCoordinate(label, vm.CrosshairPosition.Ra, vm.CrosshairPosition.Dec, vm.FilePath);
    }

    private void OnGoToManual(object s, RoutedEventArgs e)
    {
        if (_activePage is null) return;
        if (!double.TryParse(ManualRaBox.Text.Trim(), out var ra) ||
            !double.TryParse(ManualDecBox.Text.Trim(), out var dec))
        {
            if (ViewModel.ActiveViewModel is not null)
                ViewModel.ActiveViewModel.StatusMessage = "Enter valid RA and Dec in degrees";
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
}
