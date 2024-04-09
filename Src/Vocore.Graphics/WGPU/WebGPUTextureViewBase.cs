using WebGPU;

namespace Vocore.Graphics;

public abstract class WebGPUTextureViewBase:GPUTextureView
{
    public abstract WGPUTextureView Native{ get; }
}