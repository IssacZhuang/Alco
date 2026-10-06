using System.Runtime.CompilerServices;
using Alco.Graphics.AlcoGpu.Interop;
using static Alco.Graphics.InteropUtility;

namespace Alco.Graphics.AlcoGpu;

/// <summary>Describes AlcoGpuFrameBufferBase.</summary>
internal abstract class AlcoGpuFrameBufferBase : GPUFrameBuffer
{
    /// <summary>The pre-baked render pass descriptor (attachment views are refreshed per frame).</summary>
    public abstract AlcoGPU.RenderPassDesc Native { get; }
    /// <summary>Gets the native color attachment formats.</summary>
    public abstract ReadOnlySpan<PixelFormat> NativeColorFormats { get; }
    /// <summary>Gets the native depth-stencil attachment format, if present.</summary>
    public abstract PixelFormat? NativeDepthFormat { get; }

    protected AlcoGpuFrameBufferBase(in FrameBufferDescriptor descriptor) : base(descriptor)
    {
    }

    protected TextureDescriptor BuildColorTextureDescriptor(in PixelFormat format, uint width, uint height)
    {
        return new TextureDescriptor(
            TextureDimension.Texture2D,
            format,
            width,
            height,
            1,
            1,
            ColorAttachmentUsage,
            1,
            $"{Name}_color_texture"
        );
    }

    protected TextureDescriptor BuildDepthTextureDescriptor(in PixelFormat format, uint width, uint height)
    {
        return new TextureDescriptor(
            TextureDimension.Texture2D,
            format,
            width,
            height,
            1,
            1,
            DepthAttachmentUsage,
            1,
            $"{Name}_depth_texture"
        );
    }

    /// <summary>
    /// Allocates and bakes the pre-filled pass color attachments shared by the frame buffer
    /// implementations. Load op defaults to Load; the command buffer overrides clears.
    /// </summary>
    /// <returns>The pointer to native memory owned by the caller and must be freed manually.</returns>
    protected static unsafe AlcoGPU.ColorAttachment* AllocColorAttachments(
        ReadOnlySpan<GPUTextureView> colorViews,
        ReadOnlySpan<ColorAttachmentInfo> colorInfos)
    {
        AlcoGPU.ColorAttachment* colorAttachments = Alloc<AlcoGPU.ColorAttachment>(colorViews.Length);
        try
        {
        for (int i = 0; i < colorViews.Length; i++)
        {
            AlcoGPU.ColorAttachment attachment = new()
            {
                View = ((AlcoGpuTextureViewBase)colorViews[i]).Native,
                ResolveView = AlcoGPU.TextureViewHandle.Null,
                LoadOp = 0, // load
                StoreOp = 0, // store
            };
            attachment.ClearColor[0] = colorInfos[i].ClearColor.X;
            attachment.ClearColor[1] = colorInfos[i].ClearColor.Y;
            attachment.ClearColor[2] = colorInfos[i].ClearColor.Z;
            attachment.ClearColor[3] = colorInfos[i].ClearColor.W;
            colorAttachments[i] = attachment;
        }
        return colorAttachments;
        }
        catch
        {
            Free(colorAttachments);
            throw;
        }
    }

    /// <summary>
    /// Allocates and bakes the pass depth-stencil attachment. Missing and read-only channels
    /// use the AlcoGPU.None sentinel; only present, writable aspects receive load/store ops.
    /// </summary>
    protected static unsafe AlcoGPU.DepthStencilAttachment* AllocDepthAttachment(
        GPUTextureView depthStencilView,
        in DepthAttachmentInfo depthInfo)
    {
        AlcoGPU.DepthStencilAttachment* depthAttachment = Alloc<AlcoGPU.DepthStencilAttachment>(1);
        try
        {
        *depthAttachment = new AlcoGPU.DepthStencilAttachment
        {
            View = ((AlcoGpuTextureViewBase)depthStencilView).Native,
            DepthLoadOp = depthInfo.IsDepthReadOnly ? AlcoGPU.None : 0,
            DepthStoreOp = depthInfo.IsDepthReadOnly ? AlcoGPU.None : 0,
            DepthClear = depthInfo.ClearDepth,
            StencilLoadOp = depthInfo.IsStencilReadOnly ? AlcoGPU.None : 0,
            StencilStoreOp = depthInfo.IsStencilReadOnly ? AlcoGPU.None : 0,
            StencilClear = depthInfo.ClearStencil,
        };
        return depthAttachment;
        }
        catch
        {
            Free(depthAttachment);
            throw;
        }
    }

    protected static PixelFormat[] GetNativeColorFormats(AlcoGpuAttachmentLayout attachmentLayout)
    {
        PixelFormat[] colors = new PixelFormat[attachmentLayout.ColorInfos.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            colors[i] = attachmentLayout.ColorInfos[i].Format;
        }
        return colors;
    }
}
