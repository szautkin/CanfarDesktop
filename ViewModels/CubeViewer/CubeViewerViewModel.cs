using CommunityToolkit.Mvvm.ComponentModel;
using CanfarDesktop.Services.Fits;

namespace CanfarDesktop.ViewModels.CubeViewer;

/// <summary>
/// View model for the 3D Cube Viewer. Owns the orbit-camera state and the volume
/// render parameters (window, stretch, density, MIP, spectral scale) that the
/// <c>CubeVolumeRenderer</c> reads each frame. Ported from the macOS
/// <c>CubeViewerModel</c>: same orbit/zoom sensitivities, elevation clamp, and
/// default values so the Windows viewer feels identical.
/// </summary>
/// <remarks>
/// Increment 1 renders a synthetic procedural volume; real FITS NAXIS3 ingest is
/// a TODO (see <c>VolumeData.GenerateSyntheticNebula</c>). Quantitative slice
/// readouts, WCS, the transfer-function editor, and playback are intentionally
/// out of scope for this increment.
/// </remarks>
public sealed partial class CubeViewerViewModel : ObservableObject
{
    // ── Orbit camera (matches CubeViewerModel.swift defaults) ──
    [ObservableProperty]
    private float _cameraAzimuth = 0.7f;

    [ObservableProperty]
    private float _cameraElevation = 0.5f;

    [ObservableProperty]
    private float _cameraDistance = 2.6f;

    // ── Volume render parameters ──
    [ObservableProperty]
    private float _windowLo;

    [ObservableProperty]
    private float _windowHi = 1f;

    [ObservableProperty]
    private float _density = 1.0f;

    [ObservableProperty]
    private float _spectralScale = 1.5f;

    [ObservableProperty]
    private bool _mip;

    [ObservableProperty]
    private ImageStretcher.StretchMode _stretch = ImageStretcher.StretchMode.Linear;

    [ObservableProperty]
    private bool _autoOrbit;

    /// <summary>Ray-march step count (volume quality). Higher = sharper but slower.</summary>
    [ObservableProperty]
    private float _volumeSteps = 384f;

    /// <summary>Name of the loaded volume (for the status line).</summary>
    [ObservableProperty]
    private string _volumeName = "";

    /// <summary>
    /// Stretch index matching <see cref="ImageStretcher.StretchMode"/> declaration
    /// order (Linear, Log, Sqrt, Squared, Asinh), fed to the HLSL shader so the
    /// volume applies the identical stretch as the 2D slice viewer.
    /// </summary>
    public int StretchIndex => (int)Stretch;

    /// <summary>
    /// Orbit the camera by a pixel delta. Direct port of the Swift
    /// <c>orbitCamera(dx:dy:)</c>: azimuth -= dx·0.01, elevation += dy·0.01
    /// clamped to ±1.4 rad (so the camera never flips over the poles).
    /// </summary>
    public void OrbitCamera(float dx, float dy)
    {
        CameraAzimuth -= dx * 0.01f;
        CameraElevation = Math.Clamp(CameraElevation + dy * 0.01f, -1.4f, 1.4f);
    }

    /// <summary>
    /// Zoom by a scroll delta. Port of the Swift <c>zoomCamera</c>: multiplies the
    /// distance by exp(delta) and clamps to [0.5, 8].
    /// </summary>
    public void ZoomCamera(float delta)
    {
        CameraDistance = Math.Clamp(CameraDistance * MathF.Exp(delta), 0.5f, 8f);
    }

    /// <summary>Advance the auto-orbit by one tick (matches the Swift 0.0016 rad/frame drift).</summary>
    public void AdvanceAutoOrbit()
    {
        if (AutoOrbit)
            CameraAzimuth += 0.0016f;
    }

    /// <summary>Reset the camera to the default framing.</summary>
    public void ResetCamera()
    {
        CameraAzimuth = 0.7f;
        CameraElevation = 0.5f;
        CameraDistance = 2.6f;
    }
}
