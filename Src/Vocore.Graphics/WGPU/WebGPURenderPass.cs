
using System.Numerics;
using System.Runtime.CompilerServices;
using WebGPU;
using static WebGPU.WebGPU;

namespace Vocore.Graphics.WebGPU;

internal class WebGPURenderPass : GPURenderPass
{
    #region Properties
    private readonly WGPUDevice _nativeDevice;
    private RenderPassDescriptor _abstractDescriptor;

    //the texture view are not setted in the WebGPURenderPass object, these attachments are used to create the framebuffer
    private readonly WGPURenderPassColorAttachment[] _colorAttachments;
    private readonly WGPURenderPassDepthStencilAttachment? _depthAttachment;

    #endregion

    #region Abstract Implementation

    public override string Name
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _abstractDescriptor.Name;
    }

    public override IReadOnlyList<ColorAttachment> Colors
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _abstractDescriptor.Colors;
    }

    public override DepthAttachment? Depth
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _abstractDescriptor.Depth;
    }

    protected override void Dispose(bool disposing)
    {
        // Nothing to do because only meta data inside
    }

    public override GPUFrameBuffer CreateFrameBuffer(uint width, uint height, string? name = null)
    {
        if (name == null)
        {
            name = $"{Name} - FrameBuffer";
        }

        return new WebGPUFrameBuffer(this, width, height, name);
    }

    #endregion

    #region WebGPU Implementation

    internal IReadOnlyList<WGPURenderPassColorAttachment> WebGPUColorAttachments
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _colorAttachments;
    }

    internal WGPURenderPassDepthStencilAttachment? WebGPUDepthAttachment
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depthAttachment;
    }

    internal WGPUDevice NativeDevice
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _nativeDevice;
    }

    // used by WebGPU device to create surface render pass
    internal unsafe WebGPURenderPass(WGPUDevice nativeDevice,
        WGPURenderPassColorAttachment surfaceColor,
        WGPUTextureFormat surfaceFormat,
        WGPURenderPassDepthStencilAttachment? depthAttachment,
        WGPUTextureFormat depthFormat,
     string name)
    {

        ColorAttachment[] colors = new ColorAttachment[1];
        DepthAttachment? depth = null;


        WGPUColor clearColor = surfaceColor.clearValue;
        colors[0] = new ColorAttachment
        {
            Format = UtilsWebGPU.PixelFormatToAbstract(surfaceFormat),
            ClearColor = new Vector4(
                (float)clearColor.r,
                (float)clearColor.g,
                (float)clearColor.b,
                (float)clearColor.a
            )
        };


        if (depthAttachment.HasValue)
        {
            depth = new DepthAttachment
            {
                Format = UtilsWebGPU.PixelFormatToAbstract(depthFormat),
                ClearDepth = depthAttachment.Value.depthClearValue,
                ClearStencil = depthAttachment.Value.stencilClearValue,
            };
        }

        _abstractDescriptor = new RenderPassDescriptor
        {
            Name = name,
            Colors = colors,
            Depth = depth,
        };

        _nativeDevice = nativeDevice;
        _colorAttachments = new WGPURenderPassColorAttachment[1] { surfaceColor };
        _depthAttachment = depthAttachment;
    }

    // for GPUDevice.CreateRenderPass(RenderPassDescriptor)
    public unsafe WebGPURenderPass(WGPUDevice nativeDevice, in RenderPassDescriptor descriptor)
    {
        int colorCount = _abstractDescriptor.Colors.Length;

        _abstractDescriptor = descriptor;
        _nativeDevice = nativeDevice;

        _colorAttachments = new WGPURenderPassColorAttachment[colorCount];
        for (int i = 0; i < colorCount; i++)
        {
            _colorAttachments[i] = new WGPURenderPassColorAttachment
            {
                // TextureView to be filled when create Framebuffer
                view = WGPUTextureView.Null,
                resolveTarget = WGPUTextureView.Null,
                loadOp = WGPULoadOp.Clear,
                storeOp = WGPUStoreOp.Store,
                clearValue = new WGPUColor
                {
                    r = 0,
                    g = 0,
                    b = 0,
                    a = 0,
                },
            };
        }

        if (descriptor.Depth.HasValue)
        {
            _depthAttachment = new WGPURenderPassDepthStencilAttachment
            {
                // TextureView to be filled when create Framebuffer
                view = WGPUTextureView.Null,
                depthLoadOp = WGPULoadOp.Clear,
                depthStoreOp = WGPUStoreOp.Store,
                depthClearValue = descriptor.Depth.Value.ClearDepth,
                stencilLoadOp = WGPULoadOp.Clear,
                stencilStoreOp = WGPUStoreOp.Store,
                stencilClearValue = descriptor.Depth.Value.ClearStencil,
            };
        }
    }

    #endregion
}