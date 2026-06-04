using WebGPU;

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
}