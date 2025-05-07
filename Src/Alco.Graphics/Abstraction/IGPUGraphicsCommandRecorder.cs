using System.Runtime.CompilerServices;
using System;

namespace Alco.Graphics;

/// <summary>
/// Represents a recorder for graphics commands that can be used across different command recording types.
/// </summary>
public interface IGPUGraphicsCommandRecorder
{
    /// <summary>
    /// Sets the graphics pipeline state.
    /// </summary>
    /// <param name="pipeline">The graphics pipeline to use for subsequent drawing commands.</param>
    void SetGraphicsPipeline(GPUPipeline pipeline);

    /// <summary>
    /// Binds a vertex buffer to a specific slot.
    /// </summary>
    /// <param name="slot">The binding slot index.</param>
    /// <param name="buffer">The vertex buffer to bind.</param>
    /// <param name="offset">Offset in bytes from the start of the buffer.</param>
    /// <param name="size">Size in bytes of the buffer range to bind.</param>
    void SetVertexBuffer(uint slot, GPUBuffer buffer, ulong offset, ulong size);

    /// <summary>
    /// Binds an index buffer.
    /// </summary>
    /// <param name="buffer">The index buffer to bind.</param>
    /// <param name="format">Format of the index data.</param>
    /// <param name="offset">Offset in bytes from the start of the buffer.</param>
    /// <param name="size">Size in bytes of the buffer range to bind.</param>
    void SetIndexBuffer(GPUBuffer buffer, IndexFormat format, ulong offset, ulong size);

    /// <summary>
    /// Binds a resource group to a specific slot for graphics operations.
    /// </summary>
    /// <param name="slot">The binding slot index.</param>
    /// <param name="resourceGroup">The resource group containing shader resources.</param>
    void SetGraphicsResources(uint slot, GPUResourceGroup resourceGroup);

    /// <summary>
    /// Records a non-indexed draw command.
    /// </summary>
    void Draw(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance);

    /// <summary>
    /// Records an indexed draw command.
    /// </summary>
    void DrawIndexed(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance);

    /// <summary>
    /// Records an indirect draw command using buffer parameters.
    /// </summary>
    void DrawIndirect(GPUBuffer indirectBuffer, uint offset);

    /// <summary>
    /// Records an indirect indexed draw command using buffer parameters.
    /// </summary>
    void DrawIndexedIndirect(GPUBuffer indirectBuffer, uint offset);

    /// <summary>
    /// Pushes graphics shader constants to the command stream.
    /// </summary>
    /// <param name="stage">The shader stage to receive the constants.</param>
    /// <param name="bufferOffset">Offset in the constant buffer.</param>
    /// <param name="data">Pointer to the constant data.</param>
    /// <param name="size">Size in bytes of the constant data.</param>
    unsafe void PushGraphicsConstants(ShaderStage stage, uint bufferOffset, byte* data, uint size);

    /// <summary>
    /// Pushes graphics shader constants of a value type to the command stream.
    /// </summary>
    /// <typeparam name="T">The unmanaged value type containing constant data.</typeparam>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    unsafe void PushGraphicsConstants<T>(ShaderStage stage, uint bufferOffset, T data) where T : unmanaged;

    /// <summary>
    /// Pushes graphics shader constants of a value type to the start of the constant buffer.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    unsafe void PushGraphicsConstants<T>(ShaderStage stage, T data) where T : unmanaged;
}
