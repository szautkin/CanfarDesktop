using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using CanfarDesktop.Services.CubeViewer;
using CanfarDesktop.Services.Fits;
using CanfarDesktop.ViewModels.CubeViewer;

namespace CanfarDesktop.Views.CubeViewer;

/// <summary>
/// Hosts the GPU volume render surface (a <see cref="SwapChainPanel"/> bound to a
/// D3D11 swap chain) and drives orbit/zoom interaction plus the per-frame render
/// loop. The Windows analogue of the macOS <c>CubeVolumeView</c> +
/// <c>CubeViewerRootView</c>.
/// </summary>
/// <remarks>
/// The renderer runs entirely on the UI thread, driven by
/// <see cref="Microsoft.UI.Xaml.Media.CompositionTarget.Rendering"/> (the WinUI
/// equivalent of MetalKit's per-frame draw callback). The only heavy work — the
/// synthetic volume generation — is offloaded to a background task and the
/// result uploaded back on the UI thread, keeping the UI responsive while the
/// cube builds. Call <see cref="CleanupForClose"/> when tearing the page down to
/// release GPU resources.
/// </remarks>
public sealed partial class CubeViewerPage : UserControl
{
    public CubeViewerViewModel ViewModel { get; }

    private readonly CubeVolumeRenderer _renderer = new();
    private bool _initialized;
    private bool _renderingHooked;
    private bool _closed;

    // Orbit-drag state.
    private bool _isDragging;
    private Point _lastPointer;

    // ── Wireframe box + WCS caption overlay ──
    private CubeMetadata? _meta;
    private int _volNx = 1, _volNy = 1;
    private bool _overlayBuilt;
    private bool _captionsOn = true;
    private bool _freezeRenderLoop; // paused during export capture so volume + overlay share one camera
    private readonly Line[] _edgeLines = new Line[12];
    private readonly (TextBlock Shadow, TextBlock Main)[] _captions = new (TextBlock, TextBlock)[9];
    private readonly string?[] _captionText = new string?[9];
    private readonly double[] _captionW = new double[9];
    private readonly double[] _captionH = new double[9];
    private readonly CubeAxesOverlay.Frame _overlayFrame = new();
    private CubeColormap _currentColormap = CubeColormap.Inferno;
    private string _cubeName = "";

    public CubeViewerPage()
    {
        ViewModel = new CubeViewerViewModel();
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_closed) return;
        if (_initialized)
        {
            // The page was previously initialized and merely re-attached to the tree
            // (e.g. after an Unloaded that paused us) — resume the render loop.
            HookRendering();
            return;
        }

        var (pw, ph) = PhysicalSize();
        if (!_renderer.Initialize(RenderPanel, pw, ph))
        {
            ShowFallback(_renderer.InitError ?? "No compatible Direct3D 11 device was found.");
            return;
        }

        _initialized = true;
        // Resize the back buffer + re-apply the inverse-scale transform when the DPI
        // / composition scale changes (e.g. dragged to another monitor).
        RenderPanel.CompositionScaleChanged += OnCompositionScaleChanged;
        BuildOverlayVisuals();
        PopulateColormapPicker();
        StatusText.Text = "Loading cube…";

        // Decode the cube off the UI thread, then upload + start.
        _ = LoadSyntheticVolumeAsync();
    }

    private void OnCompositionScaleChanged(SwapChainPanel sender, object args)
    {
        if (!_initialized || _closed) return;
        var (w, h) = PhysicalSize();
        _renderer.Resize(w, h);
    }

    private async Task LoadSyntheticVolumeAsync()
    {
        VolumeData volume;
        string note;
        try
        {
            var downloads = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            var cubePath = System.IO.Path.Combine(downloads, "dragons_FDF_clean_tot_Kgal.car.32bit.fits");
            if (System.IO.File.Exists(cubePath))
            {
                volume = await Task.Run(() => FitsCubeReader.Read(cubePath));
                note = $"{volume.Name} · {volume.Nx}×{volume.Ny}×{volume.Nz}";
            }
            else
            {
                volume = await Task.Run(() => VolumeData.GenerateSyntheticNebula(128));
                note = volume.Name + " (cube file not found in Downloads)";
            }
        }
        catch (Exception ex)
        {
            volume = await Task.Run(() => VolumeData.GenerateSyntheticNebula(128));
            note = "Synthetic — cube read failed: " + ex.Message;
        }
        if (_closed) return;

        _renderer.SetVolume(volume);
        _meta = volume.Meta;
        _cubeName = volume.Name;
        _volNx = volume.Nx;
        _volNy = volume.Ny;
        ViewModel.VolumeName = note;
        StatusText.Text = string.IsNullOrEmpty(_meta?.Object) ? volume.Name : _meta!.Object;
        PopulateInfoPanel(_meta);
        UpdateColorbar();

        HookRendering();
    }

    /// <summary>Fill the bottom-left info panel from the cube metadata (hidden for the synthetic volume).</summary>
    private void PopulateInfoPanel(CubeMetadata? meta)
    {
        if (meta is null)
        {
            InfoPanel.Visibility = Visibility.Collapsed;
            return;
        }

        InfoObject.Text = string.IsNullOrEmpty(meta.Object) ? ViewModel.VolumeName : meta.Object;
        InfoInstrument.Text = meta.Instrument;
        InfoInstrument.Visibility = string.IsNullOrEmpty(meta.Instrument) ? Visibility.Collapsed : Visibility.Visible;

        string dims = meta.DimensionsText;
        if (meta.RenderNx != meta.Nx || meta.RenderNy != meta.Ny || meta.RenderNz != meta.Nz)
            dims += $"  (render {meta.RenderNx}×{meta.RenderNy}×{meta.RenderNz})";
        InfoDims.Text = dims;

        bool hasUnit = !string.IsNullOrEmpty(meta.Bunit);
        InfoUnitLabel.Visibility = hasUnit ? Visibility.Visible : Visibility.Collapsed;
        InfoUnit.Visibility = hasUnit ? Visibility.Visible : Visibility.Collapsed;
        InfoUnit.Text = meta.Bunit;

        InfoRange.Text = meta.RangeText;
        InfoNan.Text = meta.NanText;
        InfoPanel.Visibility = Visibility.Visible;
    }

    private void HookRendering()
    {
        if (_renderingHooked || _closed) return;
        _renderingHooked = true;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, object e)
    {
        if (_closed || !_renderer.IsReady) return;

        // Frozen during an export capture so the offscreen volume and the overlay are
        // projected for the exact same camera (auto-orbit must not advance between them).
        if (_freezeRenderLoop) return;

        // Pause when the page is collapsed / off-screen. Navigation toggles the host
        // container's Visibility (it does NOT raise Unloaded), so without this guard the
        // render loop would keep presenting at 60fps while the user is in another module.
        if (RenderPanel.ActualWidth < 1 || RenderPanel.ActualHeight < 1) return;

        ViewModel.AdvanceAutoOrbit();
        PushRenderState();
        _renderer.Render();
        UpdateOverlay();
    }

    /// <summary>Push the current view-model state into the renderer (camera + render params).</summary>
    private void PushRenderState()
    {
        _renderer.CameraAzimuth = ViewModel.CameraAzimuth;
        _renderer.CameraElevation = ViewModel.CameraElevation;
        _renderer.CameraDistance = ViewModel.CameraDistance;
        _renderer.WindowLo = ViewModel.WindowLo;
        _renderer.WindowHi = ViewModel.WindowHi;
        _renderer.Density = ViewModel.Density;
        _renderer.SpectralScale = ViewModel.SpectralScale;
        _renderer.BaseSteps = ViewModel.VolumeSteps;
        _renderer.Stretch = ViewModel.StretchIndex;
        _renderer.Mip = ViewModel.Mip;
        _renderer.Interacting = _isDragging;
    }

    // ── Wireframe box + WCS caption overlay ────────────────────────────────────

    /// <summary>Create the 12 box edges + 9 caption labels once, parented to the overlay canvas.</summary>
    private void BuildOverlayVisuals()
    {
        if (_overlayBuilt) return;
        _overlayBuilt = true;

        var edgeBrush = ArgbBrush(0x66, 0x9F, 0xC4, 0xE8); // faint cool blue
        for (int i = 0; i < _edgeLines.Length; i++)
        {
            var ln = new Line { Stroke = edgeBrush, StrokeThickness = 1, Visibility = Visibility.Collapsed };
            _edgeLines[i] = ln;
            OverlayCanvas.Children.Add(ln);
        }

        for (int i = 0; i < _captions.Length; i++)
        {
            var shadow = MakeCaptionBlock(Microsoft.UI.Colors.Black);
            var main = MakeCaptionBlock(Microsoft.UI.Colors.White);
            _captions[i] = (shadow, main);
            OverlayCanvas.Children.Add(shadow);
            OverlayCanvas.Children.Add(main);
        }
    }

    private static TextBlock MakeCaptionBlock(Color color) => new()
    {
        FontFamily = new FontFamily("Consolas"),
        FontSize = 11,
        Foreground = new SolidColorBrush(color),
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
    };

    private void OnCaptionsToggled(object sender, RoutedEventArgs e)
        => _captionsOn = CaptionsToggle.IsOn;

    /// <summary>Recompute the projected box + captions for the current camera and lay them out.</summary>
    private void UpdateOverlay()
    {
        if (!_overlayBuilt) return;
        double w = RenderPanel.ActualWidth, h = RenderPanel.ActualHeight;

        var frame = _overlayFrame;
        CubeAxesOverlay.Build(
            frame,
            ViewModel.CameraAzimuth, ViewModel.CameraElevation, ViewModel.CameraDistance,
            ViewModel.SpectralScale, _volNx, _volNy, _meta, w, h);

        // Box edges.
        for (int i = 0; i < _edgeLines.Length; i++)
        {
            var ln = _edgeLines[i];
            if (i < frame.Edges.Count && frame.Edges[i].A.Visible && frame.Edges[i].B.Visible)
            {
                var (a, b) = frame.Edges[i];
                ln.X1 = a.X; ln.Y1 = a.Y; ln.X2 = b.X; ln.Y2 = b.Y;
                ln.Visibility = Visibility.Visible;
            }
            else ln.Visibility = Visibility.Collapsed;
        }

        // Captions.
        for (int i = 0; i < _captions.Length; i++)
        {
            var (shadow, main) = _captions[i];
            bool show = _captionsOn && i < frame.Captions.Count
                        && frame.Captions[i].At.Visible
                        && !string.IsNullOrEmpty(frame.Captions[i].Text);
            if (!show)
            {
                shadow.Visibility = Visibility.Collapsed;
                main.Visibility = Visibility.Collapsed;
                continue;
            }

            var cap = frame.Captions[i];
            // Text + style are fixed per cube — (re)configure + measure only when the
            // text for this slot actually changes (i.e. once, on load).
            if (_captionText[i] != cap.Text)
            {
                _captionText[i] = cap.Text;
                main.Text = cap.Text;
                shadow.Text = cap.Text;
                var color = cap.IsAxisName ? ArgbColor(0xFF, 0x73, 0xD9, 0xFF) : ArgbColor(0xF5, 0xFF, 0xFF, 0xFF);
                main.Foreground = new SolidColorBrush(color);
                main.FontWeight = cap.IsAxisName
                    ? Microsoft.UI.Text.FontWeights.SemiBold
                    : Microsoft.UI.Text.FontWeights.Normal;
                shadow.FontWeight = main.FontWeight;
                main.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                _captionW[i] = main.DesiredSize.Width;
                _captionH[i] = main.DesiredSize.Height;
            }

            double tw = _captionW[i], th = _captionH[i];
            double x = Math.Clamp(cap.At.X - tw / 2, 8, Math.Max(8, w - tw - 8));
            double y = Math.Clamp(cap.At.Y - th / 2, 8, Math.Max(8, h - th - 8));
            Canvas.SetLeft(main, x); Canvas.SetTop(main, y);
            Canvas.SetLeft(shadow, x + 1); Canvas.SetTop(shadow, y + 1);
            main.Visibility = Visibility.Visible;
            shadow.Visibility = Visibility.Visible;
        }
    }

    private static Color ArgbColor(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);
    private static SolidColorBrush ArgbBrush(byte a, byte r, byte g, byte b) => new(Color.FromArgb(a, r, g, b));

    // ── Sizing / DPI ──────────────────────────────────────────────────────────

    private (int w, int h) PhysicalSize()
    {
        // Size the back buffer in PHYSICAL pixels using the panel's composition
        // scale — the same factor the renderer inverts via SetMatrixTransform, so
        // the two always agree (mismatching them is what produced the dark frame).
        double sx = RenderPanel.CompositionScaleX > 0 ? RenderPanel.CompositionScaleX : (XamlRoot?.RasterizationScale ?? 1.0);
        double sy = RenderPanel.CompositionScaleY > 0 ? RenderPanel.CompositionScaleY : (XamlRoot?.RasterizationScale ?? 1.0);
        int w = (int)Math.Max(1, RenderPanel.ActualWidth * sx);
        int h = (int)Math.Max(1, RenderPanel.ActualHeight * sy);
        return (w, h);
    }

    private void OnPanelSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Keep the control panel scrollable within the viewport (independent of GPU init).
        ControlScroll.MaxHeight = Math.Max(200, e.NewSize.Height - 56);
        if (!_initialized || _closed) return;
        var (w, h) = PhysicalSize();
        _renderer.Resize(w, h);
    }


    // ── Pointer input: orbit + zoom ────────────────────────────────────────────

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(RenderPanel);
        if (!pt.Properties.IsLeftButtonPressed) return;
        _isDragging = true;
        _lastPointer = pt.Position;
        RenderPanel.CapturePointer(e.Pointer);
        // Any manual interaction cancels auto-orbit drift, matching the macOS UX.
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging) return;
        var pos = e.GetCurrentPoint(RenderPanel).Position;
        float dx = (float)(pos.X - _lastPointer.X);
        float dy = (float)(pos.Y - _lastPointer.Y);
        _lastPointer = pos;
        ViewModel.OrbitCamera(dx, dy);
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        RenderPanel.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        int delta = e.GetCurrentPoint(RenderPanel).Properties.MouseWheelDelta;
        // Match the macOS wheel sensitivity (-scrollDelta * 0.01); WinUI wheel
        // ticks are 120 per notch, so scale to a comparable exp() argument.
        ViewModel.ZoomCamera(-delta / 120f * 0.12f);
        e.Handled = true;
    }

    // ── Control panel handlers ─────────────────────────────────────────────────

    private void OnDensityChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        => ViewModel.Density = (float)e.NewValue;

    private void OnSpectralChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        => ViewModel.SpectralScale = (float)e.NewValue;

    private void OnStepsChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        => ViewModel.VolumeSteps = (float)e.NewValue;

    private void OnStretchChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StretchCombo.SelectedItem is ComboBoxItem { Tag: string tag }
            && Enum.TryParse<ImageStretcher.StretchMode>(tag, out var mode))
        {
            ViewModel.Stretch = mode;
        }
    }

    // ── Colormap + colorbar ────────────────────────────────────────────────────

    private void PopulateColormapPicker()
    {
        ColormapCombo.ItemsSource = Enum.GetValues<CubeColormap>()
            .Select(CubeColormaps.DisplayName).ToList();
        ColormapCombo.SelectedIndex = (int)ViewModel.Colormap; // fires OnColormapChanged
    }

    private void OnColormapChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ColormapCombo.SelectedIndex < 0) return;
        _currentColormap = (CubeColormap)ColormapCombo.SelectedIndex;
        ViewModel.Colormap = _currentColormap;
        var lut = CubeColormaps.Build(_currentColormap);
        _renderer.SetColormap(lut);
        UpdateColorbar(lut);
    }

    /// <summary>Rebuild the colorbar gradient from the active colormap + refresh the value labels.</summary>
    private void UpdateColorbar(byte[]? lut = null)
    {
        // The window sliders' ValueChanged can fire during XAML parse, before the
        // colorbar elements (declared later) are created — no-op until they exist.
        if (ColorbarRect is null) return;
        lut ??= CubeColormaps.Build(_currentColormap);

        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        const int stops = 17;
        for (int s = 0; s < stops; s++)
        {
            int idx = s * 255 / (stops - 1);
            int o = idx * 4;
            brush.GradientStops.Add(new GradientStop
            {
                Color = Color.FromArgb(255, lut[o], lut[o + 1], lut[o + 2]),
                Offset = s / (double)(stops - 1),
            });
        }
        ColorbarRect.Fill = brush;

        if (_meta is not null)
        {
            ColorbarLo.Text = FormatColorbarValue(_meta.ValueAtNormalized(ViewModel.WindowLo));
            ColorbarHi.Text = FormatColorbarValue(_meta.ValueAtNormalized(ViewModel.WindowHi));
        }
        else
        {
            ColorbarLo.Text = string.Empty;
            ColorbarHi.Text = string.Empty;
        }
    }

    private static string FormatColorbarValue(double v)
        => v.ToString("G4", System.Globalization.CultureInfo.InvariantCulture);

    // ── Window / levels ────────────────────────────────────────────────────────

    private bool _suppressWindow; // guard reentrancy when we set the sliders programmatically

    private const float MinWindowGap = 0.01f;

    private void OnWindowLoChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressWindow) return;
        SetWindow((float)e.NewValue, ViewModel.WindowHi);
    }

    private void OnWindowHiChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressWindow) return;
        SetWindow(ViewModel.WindowLo, (float)e.NewValue);
    }

    private void OnWindowMinMax(object sender, RoutedEventArgs e) => SetWindow(0f, 1f);
    private void OnWindowP99(object sender, RoutedEventArgs e) => SetWindow(0f, 0.99f);

    /// <summary>Apply a window, enforcing [0,1] bounds + a minimum gap, keeping sliders and VM in sync.</summary>
    private void SetWindow(float lo, float hi)
    {
        lo = Math.Clamp(lo, 0f, 1f);
        hi = Math.Clamp(hi, 0f, 1f);
        if (hi - lo < MinWindowGap)
        {
            // Preserve the minimum window even at the 0/1 edges.
            if (lo + MinWindowGap <= 1f) hi = lo + MinWindowGap;
            else { hi = 1f; lo = 1f - MinWindowGap; }
        }

        _suppressWindow = true;
        WindowLoSlider.Value = lo;
        WindowHiSlider.Value = hi;
        _suppressWindow = false;
        ViewModel.WindowLo = lo;
        ViewModel.WindowHi = hi;
        UpdateColorbar();
    }

    private void OnRenderModeChanged(object sender, SelectionChangedEventArgs e)
        => ViewModel.Mip = RenderModeCombo.SelectedIndex == 1; // 0 Emission, 1 Max-Intensity

    private void OnBackgroundChanged(object sender, SelectionChangedEventArgs e)
    {
        switch (BackgroundCombo.SelectedIndex)
        {
            case 1: _renderer.SetBackground(0f, 0f, 0f); break;            // Black
            case 2: _renderer.SetBackground(0.96f, 0.96f, 0.96f); break;   // Light
            default: _renderer.SetBackground(0.02f, 0.03f, 0.06f); break;  // Dark
        }
    }

    private void OnAutoOrbitToggled(object sender, RoutedEventArgs e)
        => ViewModel.AutoOrbit = AutoOrbitToggle.IsOn;

    private void OnResetView(object sender, RoutedEventArgs e)
        => ViewModel.ResetCamera();

    // ── Teardown ───────────────────────────────────────────────────────────────

    // The page is created once and cached by MainWindow (navigation only toggles the host
    // container's Visibility), so Unloaded is not expected during normal use. If it does fire
    // (reparenting), we only PAUSE the render loop — keeping the GPU device alive so OnLoaded
    // can resume — rather than permanently disposing, which would leave the reused page blank.
    private void OnUnloaded(object sender, RoutedEventArgs e) => PauseRendering();

    /// <summary>Unhook the per-frame render loop without releasing GPU resources (resumable).</summary>
    private void PauseRendering()
    {
        if (_renderingHooked)
        {
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnRendering;
            _renderingHooked = false;
        }
    }

    /// <summary>Stop the render loop and release all GPU resources. Idempotent. For genuine teardown.</summary>
    public void CleanupForClose()
    {
        if (_closed) return;
        _closed = true;
        PauseRendering();
        RenderPanel.CompositionScaleChanged -= OnCompositionScaleChanged;
        _renderer.Dispose();
    }

    private void ShowFallback(string message)
    {
        FallbackText.Text =
            "The 3D cube viewer needs a Direct3D 11 capable GPU.\n\n" + message;
        FallbackText.Visibility = Visibility.Visible;
        StatusText.Text = "GPU unavailable";
    }
}
