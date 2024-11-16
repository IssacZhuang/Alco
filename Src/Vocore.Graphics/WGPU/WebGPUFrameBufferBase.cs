using WebGPU;

namespace Vocore.Graphics.WebGPU;

internal abstract  class WebGPUFrameBufferBase : GPUFrameBuffer
{
    public abstract WGPURenderPassDescriptor Native { get; }
    public abstract ReadOnlySpan<WGPUTextureFormat> NativeColorFormats { get; }
    public abstract WGPUTextureFormat? NativeDepthFormat { get; }
}