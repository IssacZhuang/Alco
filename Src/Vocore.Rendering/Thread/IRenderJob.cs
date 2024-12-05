using Vocore.Graphics;

namespace Vocore.Rendering;

public interface IRenderJob
{
    void Execute(GPUCommandBuffer commandBuffer);
}