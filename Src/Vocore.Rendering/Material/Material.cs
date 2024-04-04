using Vocore.Graphics;

namespace Vocore.Rendering;

public abstract class Material
{
    public abstract void PushResourceToCommandBuffer(GPUCommandBuffer commandBuffer);
}