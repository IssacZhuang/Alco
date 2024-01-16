using System.Runtime.CompilerServices;

namespace Vocore.Graphics;

public abstract class GPUCommandBuffer : BaseGPUObject
{
    protected bool _isRecording = false;
    //API
    public abstract string Name { get; }
    public abstract bool HasBuffer { get; }
    public virtual bool IsRecording
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _isRecording;
    }

    public void Begin(GPURenderPass renderPass)
    {
        UtilsAssert.IsFalse(_isRecording, "Command buffer is already recording, you might call GPUCommandBuffer.Begin(GPURenderPass) twice before calling GPUCommandBuffer.End()");
        InternalBegin(renderPass);
    }

    public void End()
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording, you might call GPUCommandBuffer.End() twice before calling GPUCommandBuffer.Begin(GPURenderPass)");
        InternalEnd();
    }

    public void SetPipeline(GPUGraphicsPipeline pipeline)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while SetPipeline, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        InternalSetPipeline(pipeline);
    }

    public void SetVertexBuffer(uint slot, GPUBuffer buffer, ulong offset, ulong size)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while SetVertexBuffer, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        InternalSetVertexBuffer(slot, buffer, offset, size);
    }

    public void SetIndexBuffer(GPUBuffer buffer, IndexFormat format,  ulong offset, ulong size)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while SetIndexBuffer, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        InternalSetIndexBuffer(buffer, format, offset, size);
    }

    public void DrawIndexed(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while DrawIndexed, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        InternalDrawIndexed(indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
    }

    public void DrawIndirect(GPUBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while DrawIndirect, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        InternalDrawIndirect(indirectBuffer, offset, drawCount, stride);
    }

    public void DrawIndexedIndirect(GPUBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while DrawIndexedIndirect, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        InternalDrawIndexedIndirect(indirectBuffer, offset, drawCount, stride);
    }

    public unsafe void UpdateBuffer(GPUBuffer buffer, uint bufferOffset, byte* data, uint size)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while UpdateBuffer, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        InternalUpdateBuffer(buffer, bufferOffset, data, size);
    }

    // polymorphism

    public void SetVertexBuffer(uint slot, GPUBuffer buffer)
    {
        SetVertexBuffer(slot, buffer, 0, buffer.Size);
    }

    public void SetIndexBuffer(GPUBuffer buffer, IndexFormat format)
    {
        SetIndexBuffer(buffer, format, 0, buffer.Size);
    }

    public unsafe void UpdateBuffer(GPUBuffer buffer, byte* data, uint size)
    {
        UpdateBuffer(buffer, 0, data, size);
    }

    public unsafe void UpdateBuffer<T>(GPUBuffer buffer, uint bufferOffset, T data) where T : unmanaged
    {
        UpdateBuffer(buffer, bufferOffset, (byte*)&data, (uint)sizeof(T));
    }

    public unsafe void UpdateBuffer<T>(GPUBuffer buffer, T data) where T : unmanaged
    {
        UpdateBuffer(buffer, 0, (byte*)&data, (uint)sizeof(T));
    }

    public unsafe void UpdateBuffer<T>(GPUBuffer buffer, uint bufferOffset, T[] data) where T : unmanaged
    {
        fixed (T* ptr = data)
        {
            UpdateBuffer(buffer, bufferOffset, (byte*)ptr, (uint)(sizeof(T) * data.Length));
        }
    }

    public unsafe void UpdateBuffer<T>(GPUBuffer buffer, T[] data) where T : unmanaged
    {
        UpdateBuffer(buffer, 0, data);
    }


    // need to be implemented for each backend
    protected abstract void InternalBegin(GPURenderPass renderPass);
    protected abstract void InternalEnd();
    protected abstract void InternalSetPipeline(GPUGraphicsPipeline pipeline);
    protected abstract void InternalSetVertexBuffer(uint slot, GPUBuffer buffer, ulong offset, ulong size);
    protected abstract void InternalSetIndexBuffer(GPUBuffer buffer, IndexFormat format, ulong offset, ulong size);
    protected abstract void InternalDrawIndexed(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance);
    protected abstract void InternalDrawIndirect(GPUBuffer indirectBuffer, uint offset, uint drawCount, uint stride);
    protected abstract void InternalDrawIndexedIndirect(GPUBuffer indirectBuffer, uint offset, uint drawCount, uint stride);
    /// <summary>
    /// Do not store the fucking pointer when implementing, it is unsafe;<br/> Try only read data from it.
    /// </summary>
    protected abstract unsafe void InternalUpdateBuffer(GPUBuffer buffer, uint bufferOffset, byte* data, uint size);
}