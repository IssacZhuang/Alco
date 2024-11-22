using Vocore.Graphics;
using System.Runtime.CompilerServices;

namespace Vocore.Rendering;

public abstract class Material
{
    /// <summary>
    /// Get the GPU pipeline of the material.
    /// </summary>
    /// <param name="renderPass">The render pass of the render target.</param>
    /// <returns>The GPU pipeline of the material.</returns>
    public abstract GPUPipeline GetPipeline(GPURenderPass renderPass);

    /// <summary>
    /// Set the resources to the command buffer.
    /// </summary>
    /// <param name="commandBuffer">The command buffer to set the resources.</param>

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PushResourceToCommandBuffer(GPUCommandBuffer commandBuffer)
    {
        SetPipelineResources(new MaterialCommandContext(commandBuffer));
    }

    /// <summary>
    /// Set the resources to the command buffer.
    /// </summary>
    /// <param name="context">The wrapper of the GPU command buffer to limit the usage of the GPU command buffer.</param>
    protected abstract void SetPipelineResources(MaterialCommandContext context);
}