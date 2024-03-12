using WebGPU;

namespace Vocore.Graphics.WebGPU;

internal abstract class WebGPUTextureBase : GPUTexture
{
    public abstract WGPUTexture Native { get; }
}