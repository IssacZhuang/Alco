using WebGPU;

namespace Vocore.Graphics;

internal abstract class WebGPUTextureViewBase:GPUTextureView
{
    public abstract WGPUTextureView Native{ get; }
}