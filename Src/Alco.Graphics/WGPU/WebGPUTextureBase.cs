using WebGPU;

namespace Alco.Graphics.WebGPU;

internal abstract class WebGPUTextureBase : GPUTexture
{
    public abstract WGPUTexture Native { get; }

    protected WebGPUTextureBase(in TextureDescriptor descriptor): base(descriptor)
    {
    }
}