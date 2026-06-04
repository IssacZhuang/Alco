using WebGPU;

namespace Alco.Graphics;

internal abstract class WebGPUTextureViewBase:GPUTextureView
{
    public abstract WGPUTextureView Native{ get; }

    protected WebGPUTextureViewBase(in TextureViewDescriptor descriptor): base(descriptor)
    {
    }

    protected WebGPUTextureViewBase(string name): base(name)
    {
    }
}