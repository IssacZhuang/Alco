using Vocore.Graphics;

namespace Vocore.Rendering;

public abstract class Material
{
    public abstract GPUPipeline GetPipeline(GPURenderPass renderPass);
    public abstract void PushResourceToCommandBuffer(GPUCommandBuffer commandBuffer);
}