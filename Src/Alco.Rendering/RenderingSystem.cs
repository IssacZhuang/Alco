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

    //preferred
    private readonly GPUAttachmentLayout _preferredSDRPass;
    private readonly GPUAttachmentLayout _preferredHDRPass;
    private readonly GPUAttachmentLayout _preferredSDRPassWithoutDepth;
    private readonly GPUAttachmentLayout _preferredHDRPassWithoutDepth;
    private readonly GPUAttachmentLayout _preferredRGBATexturePass;
    private readonly GPUAttachmentLayout _preferredRTexturePass;
    private readonly GPUAttachmentLayout _preferredLightMapPass;

    private readonly PixelFormat _preferredSDRFormat;
    private readonly PixelFormat _preferredHDRFormat;
    private readonly PixelFormat _preferredDepthStencilFormat;

    private readonly GraphicsValueBuffer<GlobalRenderData> _globalRenderData;
    private readonly GraphicsValueBuffer<Matrix4x4> _viewProjectionMatrix;

    private readonly ConcurrentGraphicsBufferPool _bufferPool;

    // Deferred command submission domain used by the RenderGraph execution scope.
    // The cache list is resident and reused across collection scopes; the nullable field
    // doubles as the "collection active" marker checked by ScheduleCommandBuffer.
    private readonly List<GPUCommandBuffer> _commandCollectionCache = new(32);
    private List<GPUCommandBuffer>? _commandCollection;

    public GPUDevice GraphicsDevice
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _device;
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

    public PixelFormat PreferredSDRFormat
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _preferredSDRFormat;
    }

    public PixelFormat PreferredHDRFormat
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _preferredHDRFormat;
    }

    public PixelFormat PreferredDepthStencilFormat
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _preferredDepthStencilFormat;
    }

    public GPUAttachmentLayout PreferredSDRPass
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _preferredSDRPass;
    }

    public GPUAttachmentLayout PreferredSDRPassWithoutDepth
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _preferredSDRPassWithoutDepth;
    }

    public GPUAttachmentLayout PreferredHDRPass
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _preferredHDRPass;
    }

    public GPUAttachmentLayout PreferredHDRPassWithoutDepth
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _preferredHDRPassWithoutDepth;
    }

    public GPUAttachmentLayout PreferredRGBATexturePass
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _preferredRGBATexturePass;
    }

    public GPUAttachmentLayout PreferredRTexturePass
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _preferredRTexturePass;
    }

    public GPUAttachmentLayout PreferredLightMapPass
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _preferredLightMapPass;
    }

    public ConcurrentGraphicsBufferPool GraphicsBufferPool
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _bufferPool;
    }

    public IShaderCache? ShaderCache { get; }

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
        PixelFormat preferredSDRFormat, 
        PixelFormat preferredHDRFormat,
        PixelFormat preferredDepthStencilFormat,
        IShaderCache? shaderCache = null
    )
    {
        _device = device;
        _host = host;

        _preferredSDRFormat = preferredSDRFormat;
        _preferredHDRFormat = preferredHDRFormat;
        _preferredDepthStencilFormat = preferredDepthStencilFormat;

        _globalRenderData = CreateGraphicsValueBuffer<GlobalRenderData>();
        _viewProjectionMatrix = CreateGraphicsValueBuffer<Matrix4x4>();

        //2kb, 4kb, 8kb, 16kb, 32kb, 64kb, 128kb, 256kb, 512kb
        _bufferPool = new ConcurrentGraphicsBufferPool(
            this,
            2 * 1024,
            4 * 1024,
            8 * 1024,
            16 * 1024,
            32 * 1024,
            64 * 1024,
            128 * 1024,
            256 * 1024,
            512 * 1024
            );

        _preferredSDRPass = device.CreateAttachmentLayout(new AttachmentLayoutDescriptor
        (
            [new(_preferredSDRFormat)],
            new(_preferredDepthStencilFormat),
            "sdr_pass"
        ));

        _preferredHDRPass = device.CreateAttachmentLayout(new AttachmentLayoutDescriptor
        (
            [new(_preferredHDRFormat)],
            new(_preferredDepthStencilFormat),
            "hdr_pass"
        ));

        _preferredSDRPassWithoutDepth = device.CreateAttachmentLayout(new AttachmentLayoutDescriptor
        (
            [new(_preferredSDRFormat)],
            null,
            "sdr_pass_no_depth"
        ));

        _preferredHDRPassWithoutDepth = device.CreateAttachmentLayout(new AttachmentLayoutDescriptor
        (
            [new(_preferredHDRFormat)],
            null,
            "hdr_pass_no_depth"
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

        ShaderCache = shaderCache;

        _host.OnUpdate += OnUpdate;
        _host.OnDispose += OnDispose;

    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ScheduleCommandBuffer(GPUCommandBuffer commandBuffer)
    {
        if (_commandCollection != null)
        {
            _commandCollection.Add(commandBuffer);
            return;
        }
        _device.Submit(commandBuffer);
    }

    /// <summary>
    /// Begins a deferred command collection scope. Internal mechanism used by the RenderGraph execution domain:
    /// while active, every submission scheduled through <see cref="ScheduleCommandBuffer"/>
    /// (i.e. every RenderContext.End()) is collected instead of being submitted immediately,
    /// until <see cref="FlushCommandCollection"/> submits all collected command buffers in a single batch.
    /// </summary>
    /// <exception cref="InvalidOperationException">A command collection is already active; nested collections are not supported.</exception>
    internal void BeginCommandCollection()
    {
        if (_commandCollection != null)
        {
            throw new InvalidOperationException("A command collection is already active, nested command collections are not supported.");
        }
        _commandCollection = _commandCollectionCache;
    }

    /// <summary>
    /// Submits all command buffers collected since <see cref="BeginCommandCollection"/> in a single batch
    /// submission and ends the collection scope. The list is kept and reused by the next collection scope.
    /// </summary>
    /// <returns>The number of submitted command buffers.</returns>
    /// <exception cref="InvalidOperationException">No command collection is active.</exception>
    internal int FlushCommandCollection()
    {
        List<GPUCommandBuffer> collection = _commandCollection
            ?? throw new InvalidOperationException("No command collection is active, try call BeginCommandCollection() first.");
        _commandCollection = null;

        int count = collection.Count;
        if (count == 0)
        {
            return 0;
        }
        _device.Submit(CollectionsMarshal.AsSpan(collection));
        collection.Clear();
        return count;
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
        _globalRenderData.Dispose();
        _bufferPool.Dispose();
    }
}