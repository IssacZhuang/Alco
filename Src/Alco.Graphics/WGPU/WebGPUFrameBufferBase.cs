using WebGPU;
using static WebGPU.WebGPU;
using static Alco.Graphics.InteropUtility;

namespace Alco.Graphics.WebGPU;

internal abstract  class WebGPUFrameBufferBase : GPUFrameBuffer
{
    public abstract WGPURenderPassDescriptor Native { get; }
    public abstract ReadOnlySpan<WGPUTextureFormat> NativeColorFormats { get; }
    public abstract WGPUTextureFormat? NativeDepthFormat { get; }

    protected WebGPUFrameBufferBase(in FrameBufferDescriptor descriptor): base(descriptor)
    {
        
    }

    protected TextureDescriptor BuildColorTextureDescriptor(in WGPUTextureFormat format, uint width, uint height)
    {
        return new TextureDescriptor(
            TextureDimension.Texture2D,
            WebGPUUtility.PixelFormatToAbstract(format),
            width,
            height,
            1,
            1,
            ColorAttachmentUsage,
            1,
            $"{Name}_color_texture"
            );
    }

    protected TextureDescriptor BuildDepthTextureDescriptor(in WGPUTextureFormat format, uint width, uint height)
    {
        return new TextureDescriptor(
            TextureDimension.Texture2D,
            WebGPUUtility.PixelFormatToAbstract(format),
            width,
            height,
            1,
            1,
            DepthAttachmentUsage,
            1,
            $"{Name}_depth_texture"
        );
    }

    // bakes the pre-filled native render pass attachments shared by the frame buffer implementations,
    // the returned native memory is owned by the caller and must be freed manually
    protected static unsafe WGPURenderPassColorAttachment* AllocColorAttachments(
        ReadOnlySpan<GPUTextureView> colorViews,
        ReadOnlySpan<WGPUColorAttachmentInfo> colorInfos)
    {
        WGPURenderPassColorAttachment* colorAttachments = Alloc<WGPURenderPassColorAttachment>(colorViews.Length);
        for (int i = 0; i < colorViews.Length; i++)
        {
            colorAttachments[i] = new WGPURenderPassColorAttachment
            {
                view = ((WebGPUTextureViewBase)colorViews[i]).Native,
                loadOp = WGPULoadOp.Load,
                storeOp = WGPUStoreOp.Store,
                clearValue = colorInfos[i].clearColor,
                depthSlice = WGPU_DEPTH_SLICE_UNDEFINED,
            };
        }
        return colorAttachments;
    }

    protected static unsafe WGPURenderPassDepthStencilAttachment* AllocDepthAttachment(
        GPUTextureView depthStencilView,
        in WGPUDepthAttachmentInfo depthInfo)
    {
        WGPURenderPassDepthStencilAttachment* depthAttachment = Alloc<WGPURenderPassDepthStencilAttachment>(1);
        *depthAttachment = new WGPURenderPassDepthStencilAttachment
        {
            view = ((WebGPUTextureViewBase)depthStencilView).Native,
            depthLoadOp = WGPULoadOp.Load,
            depthStoreOp = WGPUStoreOp.Store,
            depthClearValue = depthInfo.clearDepth,
            stencilLoadOp = WGPULoadOp.Load,
            stencilStoreOp = WGPUStoreOp.Store,
            stencilClearValue = depthInfo.clearStencil,
        };
        return depthAttachment;
    }

    protected static WGPUTextureFormat[] GetNativeColorFormats(WebGPUAttachmentLayout attachmentLayout)
    {
        WGPUTextureFormat[] colors = new WGPUTextureFormat[attachmentLayout.WebGPUColorInfos.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            colors[i] = attachmentLayout.WebGPUColorInfos[i].format;
        }
        return colors;
    }
}