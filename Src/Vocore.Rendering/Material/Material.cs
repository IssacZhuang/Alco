using Vocore.Graphics;

namespace Vocore.Rendering;

public abstract class Material
{
    public abstract GPUPipeline Pipeline { get; }
    public abstract void PushResourceToCommandBuffer(GPUCommandBuffer commandBuffer);
}