
using System.Runtime.CompilerServices;
using WebGPU;
using static WebGPU.WebGPU;

namespace Vocore.Graphics.WebGPU;

internal class WebGPURenderPass : GPURenderPass
{

    private readonly WGPUDevice _nativeDevice;
    private RenderPassDescriptor _abstractDescriptor;

    public override string Name
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _abstractDescriptor.Name ?? string.Empty;
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

    private WGPURenderPassColorAttachment[] _colorAttachments;
    private WGPURenderPassDepthStencilAttachment? _depthAttachment;

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

    protected override void Dispose(bool disposing)
    {
        
    }

    private static TextureDescriptor BuildTextureDescriptor(in PixelFormat format, uint width, uint height)
    {
        return new TextureDescriptor
        {
            // the texture could be used as a render target, copied from, or sampled from a shader
            Usage = TextureUsage.RenderAttachment | TextureUsage.Read | TextureUsage.TextureBinding,
            Dimension = TextureDimension.Texture2D,
            Width = width,
            Height = height,
            DepthOrArrayLayer = 1,
            Format = format,
            MipLevels = 1,
            SampleCount = 1,
        };
    }
}