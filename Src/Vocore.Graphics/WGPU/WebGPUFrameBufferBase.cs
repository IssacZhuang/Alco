using WebGPU;

namespace Vocore.Graphics.WebGPU;

internal abstract  class WebGPUFrameBufferBase : GPUFrameBuffer
{
    public abstract WGPURenderPassDescriptor Native { get; }
    public abstract ReadOnlySpan<WGPUTextureFormat> NativeColorFormats { get; }
    public abstract WGPUTextureFormat? NativeDepthFormat { get; }

    protected WebGPUFrameBufferBase(in FrameBufferDescriptor descriptor): base(descriptor)
    {
    }

    protected WebGPUFrameBufferBase(string name): base(name)
    {
    }

    protected TextureDescriptor BuildColorTextureDescriptor(in WGPUTextureFormat format, uint width, uint height)
    {
        return new TextureDescriptor(
            TextureDimension.Texture2D,
            UtilsWebGPU.PixelFormatToAbstract(format),
            width,
            height,
            1,
            1,
            TextureUsage.ColorAttachment | TextureUsage.TextureBinding | TextureUsage.StorageBinding | TextureUsage.Read,
            1,
            $"{Name}_color_texture"
            );
    }

    protected TextureDescriptor BuildDepthTextureDescriptor(in WGPUTextureFormat format, uint width, uint height)
    {
        return new TextureDescriptor(
            TextureDimension.Texture2D,
            UtilsWebGPU.PixelFormatToAbstract(format),
            width,
            height,
            1,
            1,
            TextureUsage.DepthAttachment | TextureUsage.TextureBinding | TextureUsage.Read,
            1,
            $"{Name}_depth_texture"
        );
    }
}