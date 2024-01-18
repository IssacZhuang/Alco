
using System.Runtime.CompilerServices;
using WebGPU;
using static WebGPU.WebGPU;

namespace Vocore.Graphics.WebGPU;

internal class WebGPURenderPass : GPURenderPass
{
    #region Properties
    private readonly WGPUDevice _nativeDevice;
    private RenderPassDescriptor _abstractDescriptor;
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
        // Nothing to do
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