namespace Vocore.Rendering;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Vocore.Graphics;

/// <summary>
/// The facility to manage rendering resource and perform rendering.
/// <br/>It is a high-level encapsulation of <see cref="GPUDevice"/>.
/// </summary>
public partial class RenderingSystem
{

    private readonly GPUDevice _device;
    private readonly IRenderScheduler _renderThread;

    //preffered
    private readonly GPURenderPass _prefferedSDRPass;
    private readonly GPURenderPass _prefferedHDRPass;
    private readonly GPURenderPass _prefferedSDRPassWithoutDepth;
    private readonly GPURenderPass _prefferedHDRPassWithoutDepth;

    private readonly PixelFormat _prefferedSDRFormat;
    private readonly PixelFormat _prefferedHDRFormat;
    private readonly PixelFormat _prefferedDepthStencilFormat;

    

    public GPUDevice GraphicsDevice
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _device;
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
        get
        {
            return _prefferedSDRPass;
        }
    }

    public GPURenderPass PrefferedSDRPassWithoutDepth
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            return _prefferedSDRPassWithoutDepth;
        }
    }

    public GPURenderPass PrefferedHDRPass
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            return _prefferedHDRPass;
        }
    }

    public GPURenderPass PrefferedHDRPassWithoutDepth
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            return _prefferedHDRPassWithoutDepth;
        }
    }

    public RenderingSystem(GPUDevice device,
    IRenderScheduler renderScheduler,//the render thread need update every frame, so it is controlled a external object
    PixelFormat prefferedSDRFormat, 
    PixelFormat prefferedHDRFormat,
    PixelFormat prefferedDepthStencilFormat
    )
    {
        _device = device;
        _renderThread = renderScheduler;

        _prefferedSDRFormat = prefferedSDRFormat;
        _prefferedHDRFormat = prefferedHDRFormat;
        _prefferedDepthStencilFormat = prefferedDepthStencilFormat;

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
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ScheduleCommandBuffer(GPUCommandBuffer commandBuffer)
    {
        _renderThread.ScheduleCommandBuffer(commandBuffer);
        //_device.Submit(commandBuffer);
    }
}