namespace Alco.Rendering;

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;

/// <summary>
/// The facility to manage global rendering resource and provide the factory to create rendering resource.
/// </summary>
public partial class RenderingSystem
{

    private readonly GPUDevice _device;
    private readonly IRenderingSystemHost _host;

    //preffered
    private readonly GPURenderPass _prefferedSDRPass;
    private readonly GPURenderPass _prefferedHDRPass;
    private readonly GPURenderPass _prefferedSDRPassWithoutDepth;
    private readonly GPURenderPass _prefferedHDRPassWithoutDepth;
    private readonly GPURenderPass _prefferedRGBATexturePass;
    private readonly GPURenderPass _prefferedRTexturePass;
    private readonly GPURenderPass _prefferedLightMapPass;

    private readonly PixelFormat _prefferedSDRFormat;
    private readonly PixelFormat _prefferedHDRFormat;
    private readonly PixelFormat _prefferedDepthStencilFormat;

    private readonly GraphicsValueBuffer<GlobalRenderData> _globalRenderData;

    private readonly ConcurrentGraphicsBufferPool _bufferPool;

    public GPUDevice GraphicsDevice
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _device;
    }

    public GraphicsValueBuffer<GlobalRenderData> GlobalRenderData
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _globalRenderData;
    }

    public PixelFormat PrefferedSDRFormat
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _prefferedSDRFormat;
    }

    public PixelFormat PrefferedHDRFormat
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _prefferedHDRFormat;
    }

    public PixelFormat PrefferedDepthStencilFormat
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _prefferedDepthStencilFormat;
    }

    public GPURenderPass PrefferedSDRPass
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _prefferedSDRPass;
    }

    public GPURenderPass PrefferedSDRPassWithoutDepth
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _prefferedSDRPassWithoutDepth;
    }

    public GPURenderPass PrefferedHDRPass
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _prefferedHDRPass;
    }

    public GPURenderPass PrefferedHDRPassWithoutDepth
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _prefferedHDRPassWithoutDepth;
    }

    public GPURenderPass PrefferedRGBATexturePass
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _prefferedRGBATexturePass;
    }

    public GPURenderPass PrefferedRTexturePass
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _prefferedRTexturePass;
    }

    public GPURenderPass PrefferedLightMapPass
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _prefferedLightMapPass;
    }

    public ConcurrentGraphicsBufferPool GraphicsBufferPool
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _bufferPool;
    }

    public GraphicsBuffer? MainCamera { get; set; }

    public RenderingSystem(
        IRenderingSystemHost host,
        GPUDevice device,
        PixelFormat prefferedSDRFormat, 
        PixelFormat prefferedHDRFormat,
        PixelFormat prefferedDepthStencilFormat
    )
    {
        _device = device;
        _host = host;

        _prefferedSDRFormat = prefferedSDRFormat;
        _prefferedHDRFormat = prefferedHDRFormat;
        _prefferedDepthStencilFormat = prefferedDepthStencilFormat;

        _globalRenderData = CreateGraphicsValueBuffer<GlobalRenderData>();

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

        _prefferedSDRPass = device.CreateRenderPass(new RenderPassDescriptor
        (
            [new(_prefferedSDRFormat)],
            new(_prefferedDepthStencilFormat),
            "sdr_pass"
        ));

        _prefferedHDRPass = device.CreateRenderPass(new RenderPassDescriptor
        (
            [new(_prefferedHDRFormat)],
            new(_prefferedDepthStencilFormat),
            "hdr_pass"
        ));

        _prefferedSDRPassWithoutDepth = device.CreateRenderPass(new RenderPassDescriptor
        (
            [new(_prefferedSDRFormat)],
            null,
            "sdr_pass_no_depth"
        ));

        _prefferedHDRPassWithoutDepth = device.CreateRenderPass(new RenderPassDescriptor
        (
            [new(_prefferedHDRFormat)],
            null,
            "hdr_pass_no_depth"
        ));

        _prefferedRGBATexturePass = device.CreateRenderPass(new RenderPassDescriptor
        (
            [new(PixelFormat.RGBA8Unorm)],
            null,
            "rgba_texture_pass"
        ));

        _prefferedRTexturePass = device.CreateRenderPass(new RenderPassDescriptor
        (
            [new(PixelFormat.R8Unorm)],
            null,
            "r_texture_pass"
        ));

        _prefferedLightMapPass = device.CreateRenderPass(new RenderPassDescriptor
        (
            [new(PixelFormat.RGBA16Float)],
            null,
            "light_map_pass"
        ));

        _host.OnUpdate += OnUpdate;
        _host.OnDispose += OnDispose;

    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ScheduleCommandBuffer(GPUCommandBuffer commandBuffer)
    {
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
    }

    private void OnDispose()
    {
        _globalRenderData.Dispose();
        _bufferPool.Dispose();
        _host.OnUpdate -= OnUpdate;
        _host.OnDispose -= OnDispose;
    }
}