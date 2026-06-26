using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;
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

    public CubeViewerPage()
    {
        ViewModel = new CubeViewerViewModel();
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized || _closed) return;

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
        StatusText.Text = "Building synthetic volume…";

        // Generate the procedural nebula off the UI thread, then upload + start.
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
        // TODO(FITS ingest): swap this for real NAXIS3 cube ingest — decode the
        // FITS 3D array, normalize against robust cut levels, down-sample to a
        // GPU-friendly size, convert to Half, and hand a VolumeData here.
        VolumeData volume = await Task.Run(() => VolumeData.GenerateSyntheticNebula(128));
        if (_closed) return;

        _renderer.SetVolume(volume);
        ViewModel.VolumeName = volume.Name;
        StatusText.Text = volume.Name;

        HookRendering();
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

        ViewModel.AdvanceAutoOrbit();

        // Push the current view-model state into the renderer, then draw.
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

        _renderer.Render();
    }

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

    private void OnMipToggled(object sender, RoutedEventArgs e)
        => ViewModel.Mip = MipToggle.IsOn;

    private void OnAutoOrbitToggled(object sender, RoutedEventArgs e)
        => ViewModel.AutoOrbit = AutoOrbitToggle.IsOn;

    private void OnResetView(object sender, RoutedEventArgs e)
        => ViewModel.ResetCamera();

    // ── Teardown ───────────────────────────────────────────────────────────────

    private void OnUnloaded(object sender, RoutedEventArgs e) => CleanupForClose();

    /// <summary>Stop the render loop and release all GPU resources. Idempotent.</summary>
    public void CleanupForClose()
    {
        if (_closed) return;
        _closed = true;
        if (_renderingHooked)
        {
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnRendering;
            _renderingHooked = false;
        }
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
