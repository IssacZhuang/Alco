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
    public readonly void SetGraphicsPipeline(GPUPipeline pipeline)
    {
        _commandBuffer.SetGraphicsPipeline(pipeline);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void SetGraphicsResources(uint slot, GPUResourceGroup resourceGroup)
    {
        _commandBuffer.SetGraphicsResources(slot, resourceGroup);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void PushConstants<T>(ShaderStage stage, T data) where T : unmanaged
    {
        _commandBuffer.PushConstants(stage, 0, data);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void PushConstants<T>(ShaderStage stage, uint bufferOffset, T data) where T : unmanaged
    {
        _commandBuffer.PushConstants(stage, bufferOffset, data);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly unsafe void PushConstants(ShaderStage stage, uint bufferOffset, byte* data, uint size)
    {
        _commandBuffer.PushConstants(stage, bufferOffset, data, size);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void SetStencilReference(uint value)
    {
        _commandBuffer.SetStencilReference(value);
    }

}