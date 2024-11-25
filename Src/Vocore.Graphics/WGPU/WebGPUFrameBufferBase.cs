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
}