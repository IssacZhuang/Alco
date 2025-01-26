using Alco.Graphics;

namespace Alco.Rendering;

public interface IRenderJob
{
    void Execute(GPUCommandBuffer commandBuffer);
}