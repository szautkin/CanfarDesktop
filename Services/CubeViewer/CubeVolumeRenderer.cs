using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;
using WinRT;
using SharpGen.Runtime;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using D3DUsage = Vortice.Direct3D11.Usage;
using DxgiUsage = Vortice.DXGI.Usage;

namespace CanfarDesktop.Services.CubeViewer;

/// <summary>
/// Direct3D 11 GPU volume ray-marcher for the Cube Viewer, bound to a WinUI
/// <c>SwapChainPanel</c> via a composition swap chain. This is the Windows port
/// of the macOS <c>CubeVolumeRenderer</c> (Metal): it uploads a half-float
/// <c>Texture3D</c> plus a colormap and opacity-transfer-function texture, then
/// renders a fullscreen-triangle pass that reconstructs the world ray from the
/// inverse view-projection and composites front-to-back.
/// </summary>
/// <remarks>
/// All D3D calls happen on the UI thread (driven from a
/// <c>CompositionTarget.Rendering</c> tick in the host page), matching how the
/// Metal renderer ran on MetalKit's main-thread draw callback. The renderer owns
/// its device/context/swap chain and is <see cref="IDisposable"/>; the host page
/// must call <see cref="Dispose"/> when the page is torn down to release VRAM
/// (a 128³ R16 volume is ~4&#160;MB, but real cubes can be far larger).
/// </remarks>
public sealed class CubeVolumeRenderer : IDisposable
{
    // ── Constant buffer (matches cbuffer CubeUniforms in the HLSL, 16-byte aligned) ──
    [StructLayout(LayoutKind.Sequential)]
    private struct CubeUniforms
    {
        public Matrix4x4 InvViewProj;   // 64
        public Matrix4x4 InverseModel;  // 64
        public Vector2 Window;          // 8
        public float Steps;             // 4
        public float Density;           // 4
        public float Jitter;            // 4
        public int Stretch;             // 4
        public int Mip;                 // 4
        public int Debug;               // 4  → total 160 (multiple of 16)
    }

    private ID3D11Device _device = null!;
    private ID3D11DeviceContext _context = null!;
    private IDXGISwapChain1 _swapChain = null!;
    private ID3D11RenderTargetView? _rtv;
    private SwapChainPanel? _panel;

    private ID3D11VertexShader _vs = null!;
    private ID3D11PixelShader _ps = null!;
    private ID3D11Buffer _cbuffer = null!;
    private ID3D11SamplerState _sampler = null!;
    private ID3D11BlendState _blend = null!;
    private ID3D11RasterizerState _raster = null!;

    private ID3D11Texture3D? _dataTex;
    private ID3D11ShaderResourceView? _dataSrv;
    private ID3D11ShaderResourceView? _cmapSrv;
    private ID3D11ShaderResourceView? _tfSrv;

    private int _width = 1;
    private int _height = 1;
    private int _volNx = 1, _volNy = 1, _volNz = 1;
    private float _jitter;
    private bool _disposed;

    /// <summary>Whether the device and pipeline initialized successfully.</summary>
    public bool IsReady { get; private set; }

    /// <summary>Set when initialization fails so the host can show a fallback.</summary>
    public string? InitError { get; private set; }

    /// <summary>TEMP diagnostic: last per-frame status (present HRESULT / not-ready reason / exception).</summary>
    public string? LastError { get; private set; }

    // ── Live parameters (pushed from the view model each frame) ──
    public float CameraAzimuth { get; set; } = 0.7f;
    public float CameraElevation { get; set; } = 0.5f;
    public float CameraDistance { get; set; } = 2.6f;
    public float WindowLo { get; set; }
    public float WindowHi { get; set; } = 1f;
    public float Density { get; set; } = 1f;
    public int Stretch { get; set; }
    public bool Mip { get; set; }
    public float BaseSteps { get; set; } = 384f;
    public float SpectralScale { get; set; } = 1.5f;
    public bool Interacting { get; set; }

    /// <summary>Diagnostic: when true the shader bypasses the volume (gradient + ray-box entry).</summary>
    public bool DebugMode { get; set; }

    /// <summary>Dark background clear color (0.02, 0.03, 0.06) from the macOS app.</summary>
    private static readonly Color4 ClearColor = new(0.02f, 0.03f, 0.06f, 1f);

    /// <summary>
    /// Initialize the device, swap chain (bound to the panel), pipeline, and
    /// static GPU resources (colormap + transfer function). Returns false and
    /// sets <see cref="InitError"/> on failure rather than throwing, so the host
    /// can degrade gracefully if no D3D11 device is available.
    /// </summary>
    /// <param name="panel">The WinUI <c>SwapChainPanel</c> the composition swap chain binds to.</param>
    /// <param name="width">Initial back-buffer width in physical pixels.</param>
    /// <param name="height">Initial back-buffer height in physical pixels.</param>
    public bool Initialize(SwapChainPanel panel, int width, int height)
    {
        try
        {
            _width = Math.Max(1, width);
            _height = Math.Max(1, height);

            var featureLevels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };
            var flags = DeviceCreationFlags.BgraSupport;
            var result = D3D11.D3D11CreateDevice(
                IntPtr.Zero, DriverType.Hardware, flags, featureLevels,
                out _device, out _context);
            if (result.Failure)
            {
                // Retry on WARP (software) so the feature still works on machines
                // without a usable hardware device (CI, RDP, headless).
                result = D3D11.D3D11CreateDevice(
                    IntPtr.Zero, DriverType.Warp, flags, featureLevels,
                    out _device, out _context);
            }
            if (result.Failure)
            {
                InitError = $"D3D11 device creation failed: {result}";
                return false;
            }

            CreateSwapChain(panel);
            CreatePipeline();
            CreateStaticResources();
            CreateRenderTarget();

            IsReady = true;
            return true;
        }
        catch (Exception ex)
        {
            InitError = ex.Message;
            return false;
        }
    }

    private void CreateSwapChain(SwapChainPanel panel)
    {
        using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
        dxgiDevice.GetAdapter(out var adapter).CheckError();
        using (adapter)
        using (var factory = adapter.GetParent<IDXGIFactory2>())
        {
            var desc = new SwapChainDescription1
            {
                Width = _width,
                Height = _height,
                Format = Format.B8G8R8A8_UNorm,
                Stereo = false,
                SampleDescription = new SampleDescription(1, 0),
                Usage = DxgiUsage.RenderTargetOutput,
                BufferCount = 2,
                Scaling = Scaling.Stretch,
                SwapEffect = SwapEffect.FlipSequential,
                AlphaMode = AlphaMode.Premultiplied,
                Flags = SwapChainFlags.None,
            };
            _swapChain = factory.CreateSwapChainForComposition(_device, desc, null);
        }

        // Bind the swap chain to the SwapChainPanel via ISwapChainPanelNative.
        // A WinUI SwapChainPanel is a CsWinRT-projected object, NOT a classic RCW,
        // so QueryInterface for the native interface with WinRT's .As<>() — the old
        // Marshal.GetObjectForIUnknown / ReleaseComObject path throws
        // "The object's type must be __ComObject…" on projected objects.
        var native = panel.As<ISwapChainPanelNative>();
        Marshal.ThrowExceptionForHR(native.SetSwapChain(_swapChain.NativePointer));

        _panel = panel;
        ApplyCompositionScale();
    }

    /// <summary>
    /// Map the physical-pixel back buffer onto the panel's DIP bounds via the
    /// inverse composition scale. REQUIRED for SwapChainPanel + a composition swap
    /// chain: without it, on a scaled display (e.g. 150%) the compositor shows only
    /// the top-left fraction of the render — which for a centred volume reads as an
    /// empty dark frame. Re-applied on resize / DPI change.
    /// </summary>
    private void ApplyCompositionScale()
    {
        if (_panel is null || _swapChain is null) return;
        float sx = _panel.CompositionScaleX > 0 ? _panel.CompositionScaleX : 1f;
        float sy = _panel.CompositionScaleY > 0 ? _panel.CompositionScaleY : 1f;
        using var sc2 = _swapChain.QueryInterface<IDXGISwapChain2>();
        sc2.MatrixTransform = new Matrix3x2(1f / sx, 0f, 0f, 1f / sy, 0f, 0f);
    }

    private void CreatePipeline()
    {
        Compile(CubeVolumeShaders.Source, CubeVolumeShaders.VertexEntry, "vs_5_0", out var vsBlob);
        Compile(CubeVolumeShaders.Source, CubeVolumeShaders.PixelEntry, "ps_5_0", out var psBlob);
        using (vsBlob)
        using (psBlob)
        {
            _vs = _device.CreateVertexShader(vsBlob.BufferPointer, vsBlob.BufferSize, null);
            _ps = _device.CreatePixelShader(psBlob.BufferPointer, psBlob.BufferSize, null);
        }

        // Constant buffer (dynamic — updated each frame via Map/Unmap).
        var cbDesc = new BufferDescription(
            Marshal.SizeOf<CubeUniforms>(),
            BindFlags.ConstantBuffer,
            D3DUsage.Dynamic,
            ResourceOptionFlags.None,
            0)
        {
            CpuAccessFlags = CpuAccessFlags.Write,
        };
        _cbuffer = _device.CreateBuffer(new CubeUniforms[1], cbDesc);

        // Linear filter, clamp-to-edge sampler (matches the Metal sampler).
        _sampler = _device.CreateSamplerState(new SamplerDescription(
            Filter.MinMagMipLinear,
            TextureAddressMode.Clamp, TextureAddressMode.Clamp, TextureAddressMode.Clamp,
            0f, 1, ComparisonFunction.Never, 0f, float.MaxValue));

        // Premultiplied "over" blend (shader outputs premultiplied (acc, alpha)).
        var blendDesc = new BlendDescription();
        blendDesc.RenderTarget[0] = new RenderTargetBlendDescription
        {
            IsBlendEnabled = true,
            SourceBlend = Blend.One,
            DestinationBlend = Blend.InverseSourceAlpha,
            BlendOperation = BlendOperation.Add,
            SourceBlendAlpha = Blend.One,
            DestinationBlendAlpha = Blend.InverseSourceAlpha,
            BlendOperationAlpha = BlendOperation.Add,
            RenderTargetWriteMask = ColorWriteEnable.All,
        };
        _blend = _device.CreateBlendState(blendDesc);

        // No back-face culling. THE BUG: with the default rasterizer state (cull back), the
        // fullscreen triangle's winding is back-facing → zero fragments → the magenta clear showed
        // but the draw produced nothing. A fullscreen pass must never cull.
        _raster = _device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid));
    }

    private static void Compile(string source, string entry, string profile, out Blob blob)
    {
        var hr = Compiler.Compile(source, entry, "CubeVolume.hlsl", profile, out blob, out var errors);
        using (errors)
        {
            if (hr.Failure)
            {
                var msg = errors is not null ? errors.ConvertToString() : hr.ToString();
                throw new InvalidOperationException($"HLSL compile failed ({entry}): {msg}");
            }
        }
    }

    private void CreateStaticResources()
    {
        _cmapSrv = CreateLut1D(CubeColormaps.Inferno(), Format.R8G8B8A8_UNorm, 4);
        SetTransferFunction(CubeColormaps.DefaultTransferFunction);
    }

    /// <summary>Build a 256×1 shader resource view from a packed LUT byte array.</summary>
    private ID3D11ShaderResourceView CreateLut1D(byte[] data, Format format, int bytesPerTexel)
    {
        var desc = new Texture2DDescription(
            format, 256, 1, 1, 1,
            BindFlags.ShaderResource, D3DUsage.Immutable, CpuAccessFlags.None, 1, 0,
            ResourceOptionFlags.None);

        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            var sub = new SubresourceData(handle.AddrOfPinnedObject(), 256 * bytesPerTexel, 0);
            using var tex = _device.CreateTexture2D(desc, new[] { sub });
            return _device.CreateShaderResourceView(tex);
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>Rebuild the opacity transfer-function texture from control points.</summary>
    public void SetTransferFunction(IReadOnlyList<Vector2> points)
    {
        _tfSrv?.Dispose();
        _tfSrv = CreateLut1D(CubeColormaps.TransferRamp(points), Format.R8_UNorm, 1);
    }

    /// <summary>
    /// Upload a new volume as an R16_FLOAT Texture3D and create its shader
    /// resource view, releasing any previous volume. The box aspect ratio is
    /// derived from the volume dimensions in <see cref="BuildModelMatrix"/>.
    /// </summary>
    public void SetVolume(VolumeData volume)
    {
        _dataSrv?.Dispose();
        _dataTex?.Dispose();

        _volNx = volume.Nx;
        _volNy = volume.Ny;
        _volNz = volume.Nz;

        var desc = new Texture3DDescription(
            Format.R16_Float, volume.Nx, volume.Ny, volume.Nz, 1,
            BindFlags.ShaderResource, D3DUsage.Immutable, CpuAccessFlags.None,
            ResourceOptionFlags.None);

        // Half is 2 bytes; row pitch = nx*2, slice pitch = nx*ny*2.
        var handle = GCHandle.Alloc(volume.Data, GCHandleType.Pinned);
        try
        {
            var sub = new SubresourceData(
                handle.AddrOfPinnedObject(),
                volume.Nx * sizeof(ushort),
                volume.Nx * volume.Ny * sizeof(ushort));
            _dataTex = _device.CreateTexture3D(desc, new[] { sub });
            _dataSrv = _device.CreateShaderResourceView(_dataTex);
        }
        finally
        {
            handle.Free();
        }
    }

    private void CreateRenderTarget()
    {
        _rtv?.Dispose();
        using var backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        _rtv = _device.CreateRenderTargetView(backBuffer);
    }

    /// <summary>
    /// Resize the swap chain back buffer to the new physical pixel size. Safe to
    /// call from the host's SizeChanged handler.
    /// </summary>
    public void Resize(int width, int height)
    {
        if (!IsReady || _disposed) return;
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == _width && height == _height) return;

        _width = width;
        _height = height;
        _rtv?.Dispose();
        _rtv = null;
        _swapChain.ResizeBuffers(2, _width, _height, Format.B8G8R8A8_UNorm, SwapChainFlags.None).CheckError();
        CreateRenderTarget();
        ApplyCompositionScale();
    }

    private Matrix4x4 BuildModelMatrix()
    {
        float m = Math.Max(_volNx, _volNy);
        if (m <= 0) m = 1;
        return CubeMath.Scale(_volNx / m, _volNy / m, SpectralScale);
    }

    /// <summary>
    /// Render one frame: update the constant buffer from the current camera and
    /// live parameters, march the volume, and present. No-op if not ready or if
    /// there's no volume / render target yet.
    /// </summary>
    public void Render()
    {
        if (!IsReady || _disposed) return;
        if (_rtv is null || _dataSrv is null || _cmapSrv is null || _tfSrv is null)
        {
            LastError = $"not-ready rtv={_rtv != null} data={_dataSrv != null} cmap={_cmapSrv != null} tf={_tfSrv != null}";
            return;
        }

        try
        {
        float aspect = _height > 0 ? (float)_width / _height : 1f;
        Matrix4x4 model = BuildModelMatrix();
        Vector3 eye = CubeMath.OrbitEye(CameraAzimuth, CameraElevation, CameraDistance);
        Matrix4x4 view = CubeMath.LookAt(eye, Vector3.Zero, new Vector3(0, 1, 0));
        Matrix4x4 proj = CubeMath.Perspective(38f * MathF.PI / 180f, aspect, 0.01f, 50f);
        Matrix4x4 viewProj = CubeMath.Mul(proj, view);

        // Animate the jitter offset so banding dissolves across frames.
        _jitter = (_jitter + 17.13f) % 1024f;

        // Drop the step count while the user is actively orbiting for fluid motion.
        float steps = Interacting ? MathF.Min(160f, BaseSteps) : BaseSteps;

        var uniforms = new CubeUniforms
        {
            // The shader reads these with #pragma pack_matrix(row_major), which
            // matches System.Numerics' in-memory row-major layout, so the
            // true-math matrix is uploaded as-is (no transpose).
            InvViewProj = CubeMath.Invert(viewProj),
            InverseModel = CubeMath.Invert(model),
            Window = new Vector2(WindowLo, WindowHi),
            Steps = steps,
            Density = Density,
            Jitter = _jitter,
            Stretch = Stretch,
            Mip = Mip ? 1 : 0,
            Debug = DebugMode ? 1 : 0,
        };

        var mapped = _context.Map(_cbuffer, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        Marshal.StructureToPtr(uniforms, mapped.DataPointer, false);
        _context.Unmap(_cbuffer, 0);

        _context.ClearRenderTargetView(_rtv, ClearColor);
        _context.OMSetRenderTargets(_rtv);
        _context.RSSetViewport(new Viewport(0, 0, _width, _height, 0f, 1f));
        _context.RSSetState(_raster);
        _context.OMSetBlendState(_blend, (Color4?)null, unchecked((int)0xFFFFFFFF));

        _context.VSSetShader(_vs);
        _context.PSSetShader(_ps);
        _context.IASetInputLayout(null);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);

        _context.PSSetConstantBuffer(0, _cbuffer);
        _context.PSSetShaderResources(0, new[] { _dataSrv, _cmapSrv, _tfSrv });
        _context.PSSetSampler(0, _sampler);

        _context.Draw(3, 0);

        var pr = _swapChain.Present(1, PresentFlags.None);
        LastError = pr.Failure ? $"present 0x{pr.Code:X8}" : $"ok {_width}x{_height}";
        }
        catch (Exception ex)
        {
            LastError = "render: " + ex.Message;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        IsReady = false;

        _dataSrv?.Dispose();
        _dataTex?.Dispose();
        _cmapSrv?.Dispose();
        _tfSrv?.Dispose();
        _rtv?.Dispose();
        _sampler?.Dispose();
        _blend?.Dispose();
        _raster?.Dispose();
        _cbuffer?.Dispose();
        _vs?.Dispose();
        _ps?.Dispose();
        _swapChain?.Dispose();
        _context?.Dispose();
        _device?.Dispose();
    }
}

/// <summary>
/// COM interop for <c>ISwapChainPanelNative</c> — lets us bind a DXGI swap chain
/// to a WinUI <c>SwapChainPanel</c>. The host page obtains the panel's
/// IUnknown via <c>As&lt;ISwapChainPanelNative&gt;()</c>-style interop.
/// </summary>
[ComImport]
[Guid("63aad0b8-7c24-40ff-85a8-640d944cc325")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISwapChainPanelNative
{
    [PreserveSig]
    int SetSwapChain(IntPtr swapChain);
}
