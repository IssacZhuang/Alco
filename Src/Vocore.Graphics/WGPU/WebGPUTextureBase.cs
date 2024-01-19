using WebGPU;

namespace Vocore.Graphics.WebGPU;

public abstract class WebGPUTextureBase : GPUTexture
{
    public abstract WGPUTexture Native { get; }
}