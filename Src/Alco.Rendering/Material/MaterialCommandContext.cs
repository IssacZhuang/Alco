using Alco.Graphics;
using System.Runtime.CompilerServices;

namespace Alco.Rendering;


/// <summary>
/// The wrapper of the GPU command buffer to limit the usage of the GPU command buffer.
/// </summary>
public readonly struct MaterialCommandContext
{
    private readonly GPUCommandBuffer _commandBuffer;

    /// <summary>
    /// Initialize the material command context.
    /// </summary>
    /// <param name="commandBuffer">The GPU command buffer.</param>
    public MaterialCommandContext(GPUCommandBuffer commandBuffer)
    {
        _commandBuffer = commandBuffer;
    }

    /// <summary>
    /// Set the graphics pipeline.
    /// </summary>
    /// <param name="pipeline">The graphics pipeline.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void SetGraphicsPipeline(GPUPipeline pipeline)
    {
        _commandBuffer.SetGraphicsPipeline(pipeline);
    }

    /// <summary>
    /// Set the graphics resources.
    /// </summary>
    /// <param name="slot">The slot index.</param>
    /// <param name="resourceGroup">The resource group.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void SetGraphicsResources(uint slot, GPUResourceGroup resourceGroup)
    {
        _commandBuffer.SetGraphicsResources(slot, resourceGroup);
    }

    /// <summary>
    /// Push constants to the shader.
    /// </summary>
    /// <param name="stage">The shader stage.</param>
    /// <param name="data">The data to push.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void PushConstants<T>(ShaderStage stage, T data) where T : unmanaged
    {
        _commandBuffer.PushGraphicsConstants(stage, 0, data);
    }

    /// <summary>
    /// Push constants to the shader.
    /// </summary>
    /// <param name="stage">The shader stage.</param>
    /// <param name="bufferOffset">The buffer offset.</param>
    /// <param name="data">The data to push.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void PushConstants<T>(ShaderStage stage, uint bufferOffset, T data) where T : unmanaged
    {
        _commandBuffer.PushGraphicsConstants(stage, bufferOffset, data);
    }

    /// <summary>
    /// Push constants to the shader.
    /// </summary>
    /// <param name="stage">The shader stage.</param>
    /// <param name="bufferOffset">The buffer offset.</param>
    /// <param name="data">The data to push.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly unsafe void PushConstants(ShaderStage stage, uint bufferOffset, byte* data, uint size)
    {
        _commandBuffer.PushGraphicsConstants(stage, bufferOffset, data, size);
    }

    /// <summary>
    /// Set the stencil reference.
    /// </summary>
    /// <param name="value">The stencil reference value.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void SetStencilReference(uint value)
    {
        _commandBuffer.SetStencilReference(value);
    }

}