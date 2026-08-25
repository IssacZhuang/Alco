namespace Alco.Rendering;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Alco.Graphics;

/// <summary>
/// The facility to manage global rendering resource and provide the factory to create rendering resource.
/// </summary>
public partial class RenderingSystem
{

    private readonly GPUDevice _device;
    private readonly IRenderingSystemHost _host;
    private readonly SharedSamplers _samplerLibrary;

    //preferred
    private readonly GPUAttachmentLayout _preferredHDRPass;
    private readonly GPUAttachmentLayout _preferredRGBATexturePass;
    private readonly GPUAttachmentLayout _preferredRTexturePass;
    private readonly GPUAttachmentLayout _preferredLightMapPass;

    private readonly PixelFormat _preferredHDRFormat;
    private readonly PixelFormat _preferredDepthStencilFormat;

    private readonly GraphicsValueBuffer<GlobalRenderData> _globalRenderData;
    private readonly GraphicsValueBuffer<Matrix4x4> _viewProjectionMatrix;

    public GPUDevice GraphicsDevice
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _device;
    }

    /// <summary>
    /// The engine's shared sampler bank: the samplers every shader samples through
    /// (the <c>_samplers</c> block of <c>alco-rendering-core.slang</c>) plus the
    /// name table resolving shader member names to GPUSampler instances. Owned by
    /// the rendering system, not the GPU device — the device only creates raw
    /// samplers. The bank is immutable engine-wide state served as shared sampler
    /// bind groups; custom samplers are module-declared entries bound per material
    /// through <see cref="ShaderParameterSet.SetSampler"/>; textures never carry
    /// samplers.
    /// </summary>
    public SharedSamplers Samplers
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _samplerLibrary;
    }

    public GraphicsValueBuffer<GlobalRenderData> GlobalRenderDataBuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _globalRenderData;
    }

    public GraphicsValueBuffer<Matrix4x4> MainCameraViewProjectionBuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _viewProjectionMatrix;
    }

    /// <summary>
    /// The pixel format of the pipeline's main HDR scene target (set via
    /// <c>GraphicsSetting.PreferredHDRFormat</c>).
    /// </summary>
    public PixelFormat PreferredHDRFormat
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _preferredHDRFormat;
    }

    /// <summary>
    /// The depth-stencil format of <see cref="PreferredHDRPass"/> (set via
    /// <c>GraphicsSetting.PreferredDepthStencilFormat</c>). The deferred pipeline's
    /// own targets always use Depth32Float regardless of this value.
    /// </summary>
    public PixelFormat PreferredDepthStencilFormat
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _preferredDepthStencilFormat;
    }

    /// <summary>
    /// The canonical layout of a forward-style HDR scene target (HDR color +
    /// depth-stencil). Pass it to <see cref="RenderPipeline"/> when composing a
    /// custom pipeline whose scene texture needs its own depth attachment.
    /// </summary>
    public GPUAttachmentLayout PreferredHDRPass
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _preferredHDRPass;
    }

    /// <summary>
    /// The canonical layout of a general-purpose 8-bit offscreen texture
    /// (RGBA8Unorm, no depth) — snapshots, atlases, encoders.
    /// </summary>
    public GPUAttachmentLayout PreferredRGBATexturePass
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _preferredRGBATexturePass;
    }

    /// <summary>
    /// The canonical layout of a single-channel 8-bit offscreen texture
    /// (R8Unorm, no depth) — font SDF generation.
    /// </summary>
    public GPUAttachmentLayout PreferredRTexturePass
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _preferredRTexturePass;
    }

    /// <summary>
    /// The canonical layout of a filterable HDR compute texture (RGBA16Float,
    /// no depth) — GI/AO/SSR intermediates, light maps, post-process pyramids.
    /// </summary>
    public GPUAttachmentLayout PreferredLightMapPass
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _preferredLightMapPass;
    }

    /// <summary>
    /// The slang module system's disk-cache directory (`.slang-module` IR blobs and
    /// linked programs), or null when caching is disabled — replaces the retired
    /// Slang module/program cache (plan §4.2).
    /// </summary>
    public string? SlangCacheDirectory { get; }

    private WeakReference<ICamera>? _mainCameraWeakRef;

    /// <summary>
    /// Gets or sets the main camera for rendering.
    /// Internally stored as a weak reference, so this may return null if the camera has been collected.
    /// </summary>
    public ICamera? MainCamera
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (_mainCameraWeakRef != null && _mainCameraWeakRef.TryGetTarget(out ICamera? camera))
            {
                return camera;
            }
            return null;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            if (value == null)
            {
                _mainCameraWeakRef = null;
                return;
            }
            if (_mainCameraWeakRef == null)
            {
                _mainCameraWeakRef = new WeakReference<ICamera>(value);
            }
            else
            {
                _mainCameraWeakRef.SetTarget(value);
            }
        }
    }

    public RenderingSystem(
        IRenderingSystemHost host,
        GPUDevice device,
        PixelFormat preferredHDRFormat,
        PixelFormat preferredDepthStencilFormat,
        string? slangCacheDirectory = null
    )
    {
        _device = device;
        _host = host;
        _samplerLibrary = new SharedSamplers(device);

        _preferredHDRFormat = preferredHDRFormat;
        _preferredDepthStencilFormat = preferredDepthStencilFormat;

        _globalRenderData = CreateGraphicsValueBuffer<GlobalRenderData>();
        _viewProjectionMatrix = CreateGraphicsValueBuffer<Matrix4x4>();

        _preferredHDRPass = device.CreateAttachmentLayout(new AttachmentLayoutDescriptor
        (
            [new(_preferredHDRFormat)],
            new(_preferredDepthStencilFormat),
            "hdr_pass"
        ));

        _preferredRGBATexturePass = device.CreateAttachmentLayout(new AttachmentLayoutDescriptor
        (
            [new(PixelFormat.RGBA8Unorm)],
            null,
            "rgba_texture_pass"
        ));

        _preferredRTexturePass = device.CreateAttachmentLayout(new AttachmentLayoutDescriptor
        (
            [new(PixelFormat.R8Unorm)],
            null,
            "r_texture_pass"
        ));

        _preferredLightMapPass = device.CreateAttachmentLayout(new AttachmentLayoutDescriptor
        (
            [new(PixelFormat.RGBA16Float)],
            null,
            "light_map_pass"
        ));

        SlangCacheDirectory = slangCacheDirectory;

        _host.OnUpdate += OnUpdate;
        _host.OnDispose += OnDispose;

    }

    // Test hook: total number of command buffers submitted through ScheduleCommandBuffer.
    internal int ScheduledSubmissionCount;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ScheduleCommandBuffer(GPUCommandBuffer commandBuffer)
    {
        ScheduledSubmissionCount++;
        _device.Submit(commandBuffer);
    }

    private void OnUpdate(float deltaTime)
    {
        GlobalRenderData globleRenderData = _globalRenderData.Value;
        globleRenderData.Time += deltaTime;
        globleRenderData.DeltaTime = deltaTime;
        globleRenderData.SinTime = math.sin(globleRenderData.Time);
        globleRenderData.CosTime = math.cos(globleRenderData.Time);
        _globalRenderData.Value = globleRenderData;
        _globalRenderData.UpdateBuffer();

        ICamera? mainCamera = MainCamera;
        if (mainCamera != null)
        {
            _viewProjectionMatrix.Value = mainCamera.ViewProjectionMatrix;
            _viewProjectionMatrix.UpdateBuffer();
        }
    }

    private void OnDispose()
    {
        _host.OnUpdate -= OnUpdate;
        _host.OnDispose -= OnDispose;
        _samplerLibrary.Dispose();
        _globalRenderData.Dispose();
        _viewProjectionMatrix.Dispose();
        _preferredHDRPass.Dispose();
        _preferredRGBATexturePass.Dispose();
        _preferredRTexturePass.Dispose();
        _preferredLightMapPass.Dispose();
        OnDisposeShaderSystem();
    }
}
