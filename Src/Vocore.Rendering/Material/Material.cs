using Vocore.Graphics;
using System.Runtime.CompilerServices;

namespace Vocore.Rendering;

public abstract class Material
{
    public abstract GPUPipeline GetPipeline(GPURenderPass renderPass);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PushResourceToCommandBuffer(GPUCommandBuffer commandBuffer)
    {
        SetPipelineResources(new MaterialCommandContext(commandBuffer));
    }

    protected abstract void SetPipelineResources(MaterialCommandContext context);
}