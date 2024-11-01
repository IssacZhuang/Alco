using Vocore.Graphics;
using System.Runtime.CompilerServices;

namespace Vocore.Rendering;

public struct MaterialCommandContext
{
    private readonly GPUCommandBuffer _commandBuffer;

    public MaterialCommandContext(GPUCommandBuffer commandBuffer)
    {
        _commandBuffer = commandBuffer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetGraphicsPipeline(GPUPipeline pipeline)
    {
        _commandBuffer.SetGraphicsPipeline(pipeline);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetComputePipeline(GPUPipeline pipeline)
    {
        _commandBuffer.SetComputePipeline(pipeline);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetGraphicsResources(uint slot, GPUResourceGroup resourceGroup)
    {
        _commandBuffer.SetGraphicsResources(slot, resourceGroup);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetComputeResources(uint slot, GPUResourceGroup resourceGroup)
    {
        _commandBuffer.SetComputeResources(slot, resourceGroup);
    }

}